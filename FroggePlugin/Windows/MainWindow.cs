using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using FroggePlugin.Api;

namespace FroggePlugin.Windows;

public partial class MainWindow : Window, IDisposable
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
        Events,
        EventList,
        EventDetail,
        Profiles,
        ProfileDetail,
        Giveaways,
        GiveawayList,
        Raffles,
        RaffleList,
        Manage,
        ManageVenue,
        ProfileApprovalQueue,
        ProfileApprovalDetail,
    }

    private enum VipLoadState
    {
        Idle,
        Loading,
        Loaded,
        Error,
    }

    // --- State ---------------------------------------------------------------------------

    private readonly Plugin plugin;

    private LinkState state = LinkState.Idle;
    private string codeInput = string.Empty;
    private string? errorMessage;

    // Written only by the background link task, applied to Configuration/ApiClient only by
    // Draw() on the next frame - keeps all state mutation on the render thread.
    private PluginTokenRedeemed? pendingResult;

    private Page page = Page.Home;

    // Generic guild picker, backed by the shared /plugin/guilds endpoint - shared across every
    // feature that needs a "which venue?" step (Giveaways and Raffles so far) rather than
    // redeclared per-feature, since guild membership itself has nothing to do with which
    // feature is asking.
    private VipLoadState guildsLoadState = VipLoadState.Idle;
    private string? guildsErrorMessage;
    private List<PluginGuild>? guilds;

    public MainWindow(Plugin plugin) : base("Frogge##MainWindow")
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

        DrawTitle("Frogge");
        ImGui.Separator();
        ImGui.Spacing();

        if (plugin.Configuration.AuthToken is null)
        {
            DrawUnlinked();
        }
        else switch (page)
        {
            case Page.VipStatus:
                DrawVipStatus(); break;
            case Page.VipHistory:
                DrawVipHistory(); break;
            case Page.VipPerks:
                DrawVipPerks(); break;
            case Page.Events:
                DrawEvents(); break;
            case Page.EventList:
                DrawEventList(); break;
            case Page.EventDetail:
                DrawEventDetail(); break;
            case Page.Profiles:
                DrawProfiles(); break;
            case Page.ProfileDetail:
                DrawProfileDetail(); break;
            case Page.Giveaways:
                DrawGiveaways(); break;
            case Page.GiveawayList:
                DrawGiveawayList(); break;
            case Page.Raffles:
                DrawRaffles(); break;
            case Page.RaffleList:
                DrawRaffleList(); break;
            case Page.Manage:
                DrawManage(); break;
            case Page.ManageVenue:
                DrawManageVenue(); break;
            case Page.ProfileApprovalQueue:
                DrawProfileApprovalQueue(); break;
            case Page.ProfileApprovalDetail:
                DrawProfileApprovalDetail(); break;
            default:
                DrawHome(); break;
        }
    }

    private void DrawHome()
    {
        var label = plugin.Configuration.LinkedDiscordUsername
            ?? plugin.Configuration.LinkedDiscordUserId?.ToString()
            ?? "Unknown";
        ImGui.TextDisabled($"Linked as {label}");
        ImGui.Spacing();
        ImGui.Spacing();

        if (ColoredButton("VIP Status", AccentColor, FullWidthButton))
            StartVipStatus();
        ImGui.Spacing();

        if (ColoredButton("Events", AccentColor, FullWidthButton))
            StartEvents();
        ImGui.Spacing();

        if (ColoredButton("Profiles", AccentColor, FullWidthButton))
            StartProfiles();
        ImGui.Spacing();

        if (ColoredButton("Giveaways", AccentColor, FullWidthButton))
            StartGiveaways();
        ImGui.Spacing();

        if (ColoredButton("Raffles", AccentColor, FullWidthButton))
            StartRaffles();
        ImGui.Spacing();

        if (ColoredButton("Manage", AccentColor, FullWidthButton))
            StartManage();

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        if (ColoredButton("Forget", DangerColor, FullWidthButton))
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

    private void DrawUnlinked()
    {
        ImGui.TextWrapped("Run /plugin-link in Discord, then enter the code below.");
        ImGui.Spacing();

        ImGui.SetNextItemWidth(-1);
        ImGui.InputText("##Code", ref codeInput, 16);
        ImGui.Spacing();

        var inProgress = state == LinkState.InProgress;
        ImGui.BeginDisabled(inProgress);
        var linked = ColoredButton("Link", AccentColor, FullWidthButton);
        ImGui.EndDisabled();
        if (linked)
            StartLink();

        ImGui.Spacing();
        if (inProgress)
            ImGui.TextDisabled("Linking...");
        else if (state == LinkState.Error && errorMessage is not null)
            DrawColored(errorMessage, DangerColor);
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
