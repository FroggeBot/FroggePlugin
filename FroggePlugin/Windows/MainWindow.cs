using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using FroggePlugin.Api;

namespace FroggePlugin.Windows;

public class MainWindow : Window, IDisposable
{
    private enum LinkState
    {
        Idle,
        InProgress,
        Error,
    }

    private enum Page
    {
        Home,
        VipStatus,
        VipHistory,
        VipPerks,
    }

    private enum VipLoadState
    {
        Idle,
        Loading,
        Loaded,
        Error,
    }

    private readonly Plugin plugin;

    private LinkState state = LinkState.Idle;
    private string codeInput = string.Empty;
    private string? errorMessage;

    // Written only by the background link task, applied to Configuration/ApiClient only by
    // Draw() on the next frame - keeps all state mutation on the render thread.
    private PluginTokenRedeemed? pendingResult;

    private Page page = Page.Home;
    private VipLoadState vipLoadState = VipLoadState.Idle;
    private string? vipErrorMessage;

    // Read-only display data - unlike pendingResult above, nothing here is ever copied onto
    // Configuration/HttpClient, so the background fetch task can set it directly.
    private List<PluginVipMembership>? vipMemberships;

    // Which venue History/Perks was opened for - set when a venue card's button is clicked.
    private ulong selectedGuildId;
    private string selectedGuildName = string.Empty;

    private VipLoadState historyLoadState = VipLoadState.Idle;
    private string? historyErrorMessage;
    private List<PluginVipHistoryPeriod>? vipHistory;

    private VipLoadState perksLoadState = VipLoadState.Idle;
    private string? perksErrorMessage;
    private List<PluginVipPerkStatus>? vipPerks;

    public MainWindow(Plugin plugin) : base("FroggePlugin##MainWindow")
    {
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(375, 200),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
        };

        this.plugin = plugin;
    }

    public void Dispose() { }

    public override void Draw()
    {
        if (pendingResult is { } result)
        {
            plugin.Configuration.AuthToken = result.Token;
            plugin.Configuration.LinkedDiscordUserId = result.DiscordUserId;
            plugin.Configuration.LinkedDiscordUsername = result.DiscordUsername;
            plugin.Configuration.Save();
            plugin.ApiClient.SetAuthToken(result.Token);
            pendingResult = null;
            state = LinkState.Idle;
        }

        ImGui.Text("FroggePlugin");
        ImGui.Separator();

        if (plugin.Configuration.AuthToken is null)
        {
            DrawUnlinked();
        }
        else if (page == Page.VipStatus)
        {
            DrawVipStatus();
        }
        else if (page == Page.VipHistory)
        {
            DrawVipHistory();
        }
        else if (page == Page.VipPerks)
        {
            DrawVipPerks();
        }
        else
        {
            DrawHome();
        }
    }

    private void DrawHome()
    {
        var label = plugin.Configuration.LinkedDiscordUsername
            ?? plugin.Configuration.LinkedDiscordUserId?.ToString()
            ?? "Unknown";
        ImGui.TextWrapped($"Linked as {label}");

        if (ImGui.Button("VIP Status"))
        {
            StartVipStatus();
        }

        if (ImGui.Button("Forget"))
        {
            plugin.Configuration.AuthToken = null;
            plugin.Configuration.LinkedDiscordUserId = null;
            plugin.Configuration.LinkedDiscordUsername = null;
            plugin.Configuration.Save();
            plugin.ApiClient.SetAuthToken(null);

            // Best-effort server-side revoke; RevokeAsync swallows its own exceptions, and local
            // state above is already authoritative for the UI regardless of the outcome.
            _ = plugin.ApiClient.RevokeAsync();
        }
    }

    private void DrawVipStatus()
    {
        if (ImGui.Button("Back"))
        {
            page = Page.Home;
            vipLoadState = VipLoadState.Idle;
            vipMemberships = null;
            vipErrorMessage = null;
            return;
        }

        ImGui.Separator();

        switch (vipLoadState)
        {
            case VipLoadState.Loading:
                ImGui.TextWrapped("Loading...");
                break;

            case VipLoadState.Error:
                ImGui.TextWrapped(vipErrorMessage ?? "Something went wrong.");
                if (ImGui.Button("Retry"))
                {
                    StartVipStatus();
                }
                break;

            case VipLoadState.Loaded:
                if (vipMemberships is null || vipMemberships.Count == 0)
                {
                    ImGui.TextWrapped("You're not a VIP anywhere yet.");
                    break;
                }

                foreach (var membership in vipMemberships)
                {
                    ImGui.Text(membership.GuildName);
                    ImGui.TextWrapped($"Tier: {membership.TierName}");
                    ImGui.TextWrapped(
                        membership.ExpiresAt is { } expiresAt
                            ? $"Expires: {expiresAt.LocalDateTime:d}"
                            : "Expires: Never"
                    );

                    if (ImGui.Button($"History##{membership.GuildId}"))
                    {
                        StartVipHistory(membership.GuildId, membership.GuildName);
                    }
                    ImGui.SameLine();
                    if (ImGui.Button($"Perks##{membership.GuildId}"))
                    {
                        StartVipPerks(membership.GuildId, membership.GuildName);
                    }

                    ImGui.Separator();
                }
                break;
        }
    }

    private void StartVipStatus()
    {
        page = Page.VipStatus;
        vipLoadState = VipLoadState.Loading;
        vipErrorMessage = null;
        _ = FetchVipStatusAsync();
    }

    private async Task FetchVipStatusAsync()
    {
        try
        {
            var result = await plugin.ApiClient.GetVipMembershipsAsync();
            if (result is null)
            {
                vipErrorMessage = "Couldn't load VIP status.";
                vipLoadState = VipLoadState.Error;
                return;
            }

            vipMemberships = result;
            vipLoadState = VipLoadState.Loaded;
        }
        catch (Exception ex)
        {
            vipErrorMessage = $"Couldn't load VIP status: {ex.Message}";
            vipLoadState = VipLoadState.Error;
        }
    }

    private void DrawVipHistory()
    {
        if (ImGui.Button("Back"))
        {
            page = Page.VipStatus;
            historyLoadState = VipLoadState.Idle;
            vipHistory = null;
            historyErrorMessage = null;
            return;
        }

        ImGui.Separator();
        ImGui.Text($"{selectedGuildName} - History");

        switch (historyLoadState)
        {
            case VipLoadState.Loading:
                ImGui.TextWrapped("Loading...");
                break;

            case VipLoadState.Error:
                ImGui.TextWrapped(historyErrorMessage ?? "Something went wrong.");
                if (ImGui.Button("Retry"))
                {
                    StartVipHistory(selectedGuildId, selectedGuildName);
                }
                break;

            case VipLoadState.Loaded:
                if (vipHistory is null || vipHistory.Count == 0)
                {
                    ImGui.TextWrapped("No membership history yet.");
                    break;
                }

                foreach (var period in vipHistory)
                {
                    ImGui.Text($"Tier: {period.TierName}");
                    ImGui.TextWrapped($"Started: {period.StartedAt.LocalDateTime:d}");
                    ImGui.TextWrapped(
                        period.EndedAt is { } endedAt
                            ? $"Ended: {endedAt.LocalDateTime:d}"
                            : "Ended: Current"
                    );
                    if (period.EndedReason is not null)
                    {
                        ImGui.TextWrapped($"Reason: {period.EndedReason}");
                    }
                    ImGui.Separator();
                }
                break;
        }
    }

    private void StartVipHistory(ulong guildId, string guildName)
    {
        page = Page.VipHistory;
        selectedGuildId = guildId;
        selectedGuildName = guildName;
        historyLoadState = VipLoadState.Loading;
        historyErrorMessage = null;
        _ = FetchVipHistoryAsync(guildId);
    }

    private async Task FetchVipHistoryAsync(ulong guildId)
    {
        try
        {
            var result = await plugin.ApiClient.GetVipHistoryAsync(guildId);
            if (result is null)
            {
                historyErrorMessage = "Couldn't load VIP history.";
                historyLoadState = VipLoadState.Error;
                return;
            }

            vipHistory = result;
            historyLoadState = VipLoadState.Loaded;
        }
        catch (Exception ex)
        {
            historyErrorMessage = $"Couldn't load VIP history: {ex.Message}";
            historyLoadState = VipLoadState.Error;
        }
    }

    private void DrawVipPerks()
    {
        if (ImGui.Button("Back"))
        {
            page = Page.VipStatus;
            perksLoadState = VipLoadState.Idle;
            vipPerks = null;
            perksErrorMessage = null;
            return;
        }

        ImGui.Separator();
        ImGui.Text($"{selectedGuildName} - Perks");

        switch (perksLoadState)
        {
            case VipLoadState.Loading:
                ImGui.TextWrapped("Loading...");
                break;

            case VipLoadState.Error:
                ImGui.TextWrapped(perksErrorMessage ?? "Something went wrong.");
                if (ImGui.Button("Retry"))
                {
                    StartVipPerks(selectedGuildId, selectedGuildName);
                }
                break;

            case VipLoadState.Loaded:
                if (vipPerks is null || vipPerks.Count == 0)
                {
                    ImGui.TextWrapped("No perks for your current tier.");
                    break;
                }

                foreach (var perk in vipPerks)
                {
                    ImGui.TextWrapped($"{perk.Text} - {perk.RedemptionStatus}");
                }
                break;
        }
    }

    private void StartVipPerks(ulong guildId, string guildName)
    {
        page = Page.VipPerks;
        selectedGuildId = guildId;
        selectedGuildName = guildName;
        perksLoadState = VipLoadState.Loading;
        perksErrorMessage = null;
        _ = FetchVipPerksAsync(guildId);
    }

    private async Task FetchVipPerksAsync(ulong guildId)
    {
        try
        {
            var result = await plugin.ApiClient.GetVipPerksAsync(guildId);
            if (result is null)
            {
                perksErrorMessage = "Couldn't load VIP perks.";
                perksLoadState = VipLoadState.Error;
                return;
            }

            vipPerks = result;
            perksLoadState = VipLoadState.Loaded;
        }
        catch (Exception ex)
        {
            perksErrorMessage = $"Couldn't load VIP perks: {ex.Message}";
            perksLoadState = VipLoadState.Error;
        }
    }

    private void DrawUnlinked()
    {
        ImGui.TextWrapped("Run /plugin-link in Discord, then enter the code below.");

        ImGui.InputText("Code", ref codeInput, 16);

        var inProgress = state == LinkState.InProgress;
        ImGui.BeginDisabled(inProgress);
        if (ImGui.Button("Link"))
        {
            StartLink();
        }
        ImGui.EndDisabled();

        if (inProgress)
        {
            ImGui.TextWrapped("Linking...");
        }
        else if (state == LinkState.Error && errorMessage is not null)
        {
            ImGui.TextWrapped(errorMessage);
        }
    }

    private void StartLink()
    {
        state = LinkState.InProgress;
        errorMessage = null;
        _ = LinkAsync(codeInput);
    }

    private async Task LinkAsync(string code)
    {
        try
        {
            var result = await plugin.ApiClient.RedeemPairingCodeAsync(code.Trim().ToUpperInvariant());
            if (result is null)
            {
                errorMessage = "Invalid or expired code.";
                state = LinkState.Error;
                return;
            }

            pendingResult = result;
        }
        catch (Exception ex)
        {
            errorMessage = $"Link failed: {ex.Message}";
            state = LinkState.Error;
        }
    }
}
