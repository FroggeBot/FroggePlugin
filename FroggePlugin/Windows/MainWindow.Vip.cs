using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using FroggePlugin.Api;

namespace FroggePlugin.Windows;

public partial class MainWindow
{
    private VipLoadState vipLoadState = VipLoadState.Idle;
    private string? vipErrorMessage;

    // Read-only display data - unlike pendingResult, nothing here is ever copied onto
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

    private void DrawVipStatus()
    {
        if (DrawBackButton())
        {
            page = Page.Home;
            vipLoadState = VipLoadState.Idle;
            vipMemberships = null;
            vipErrorMessage = null;
            return;
        }

        ImGui.Separator();
        ImGui.Spacing();

        switch (vipLoadState)
        {
            case VipLoadState.Loading:
                DrawLoading();
                break;

            case VipLoadState.Error:
                DrawError(vipErrorMessage, StartVipStatus);
                break;

            case VipLoadState.Loaded:
                if (vipMemberships is null || vipMemberships.Count == 0)
                {
                    DrawEmpty("You're not a VIP anywhere yet.");
                    break;
                }

                foreach (var membership in vipMemberships)
                {
                    BeginCard();
                    DrawTitle(membership.GuildName);
                    DrawBadge(membership.TierName, AccentColor);
                    DrawColored(
                        membership.ExpiresAt is { } expiresAt
                            ? $"● Expires {expiresAt.LocalDateTime:d}"
                            : "● Never expires",
                        ExpiryColor(membership.ExpiresAt)
                    );
                    ImGui.Spacing();

                    if (ImGui.Button($"History##{membership.GuildId}"))
                        StartVipHistory(membership.GuildId, membership.GuildName);
                    ImGui.SameLine();
                    if (ImGui.Button($"Perks##{membership.GuildId}"))
                        StartVipPerks(membership.GuildId, membership.GuildName);
                    EndCard(AccentColor);
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
        if (DrawBackButton())
        {
            page = Page.VipStatus;
            historyLoadState = VipLoadState.Idle;
            vipHistory = null;
            historyErrorMessage = null;
            return;
        }

        ImGui.Separator();
        ImGui.Spacing();
        DrawTitle($"{selectedGuildName} - History");
        ImGui.Spacing();

        switch (historyLoadState)
        {
            case VipLoadState.Loading:
                DrawLoading();
                break;

            case VipLoadState.Error:
                DrawError(historyErrorMessage, () => StartVipHistory(selectedGuildId, selectedGuildName));
                break;

            case VipLoadState.Loaded:
                if (vipHistory is null || vipHistory.Count == 0)
                {
                    DrawEmpty("No membership history yet.");
                    break;
                }

                foreach (var period in vipHistory)
                {
                    var accentColor = period.EndedAt is null ? SuccessColor : MutedColor;
                    BeginCard();
                    DrawBadge(period.TierName, accentColor);
                    ImGui.TextDisabled($"Started {period.StartedAt.LocalDateTime:d}");
                    if (period.EndedAt is { } endedAt)
                        ImGui.TextDisabled($"Ended {endedAt.LocalDateTime:d}");
                    else
                        DrawColored("● Current", SuccessColor);
                    if (period.EndedReason is not null)
                        DrawColored($"Reason: {period.EndedReason}", WarningColor);
                    EndCard(accentColor);
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
        if (DrawBackButton())
        {
            page = Page.VipStatus;
            perksLoadState = VipLoadState.Idle;
            vipPerks = null;
            perksErrorMessage = null;
            return;
        }

        ImGui.Separator();
        ImGui.Spacing();
        DrawTitle($"{selectedGuildName} - Perks");
        ImGui.Spacing();

        switch (perksLoadState)
        {
            case VipLoadState.Loading:
                DrawLoading();
                break;

            case VipLoadState.Error:
                DrawError(perksErrorMessage, () => StartVipPerks(selectedGuildId, selectedGuildName));
                break;

            case VipLoadState.Loaded:
                if (vipPerks is null || vipPerks.Count == 0)
                {
                    DrawEmpty("No perks for your current tier.");
                    break;
                }

                foreach (var perk in vipPerks)
                {
                    var (glyph, color) = perk.RedemptionStatus switch
                    {
                        "Fully Redeemed" => ("✓", SuccessColor),
                        "Partially Redeemed" => ("●", WarningColor),
                        _ => ("○", MutedColor),
                    };
                    BeginCard();
                    DrawColored(glyph, color);
                    ImGui.SameLine();
                    ImGui.TextWrapped(perk.Text);
                    DrawColored(perk.RedemptionStatus, color);
                    EndCard(color);
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
}
