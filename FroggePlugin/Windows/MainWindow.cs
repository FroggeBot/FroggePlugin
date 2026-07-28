using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
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
        ManageVipRoster,
        ManageVipMemberDetail,
        ManageVipAssignTarget,
        ManageGiveawayList,
        ManageGiveawayDetail,
        ManageRaffleList,
        ManageRaffleDetail,
        ManageRaffleAssignTarget,
        ManageStaffingRoster,
        ManageStaffingMemberDetail,
        ManageStaffingHireTarget,
        ManageEventList,
        ManageEventDetail,
        ManageEventAssignSignupTarget,
        Settings,
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

    public void Dispose() => DisposeImageResources();

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

        // Every sub-screen already draws its own Separator()+Spacing() right after its Back
        // button - the native Dalamud window titlebar already reads "Frogge," so a second
        // title+separator here would just duplicate it. This is only a small breathing-room gap.
        ImGui.Spacing();

        // Reachable regardless of link state - fixing a bad ApiBaseUrl is exactly the scenario
        // where the player can't link at all yet, so Settings can't sit behind that gate.
        if (page == Page.Settings)
        {
            DrawSettings();
            return;
        }

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
            case Page.ManageVipRoster:
                DrawManageVipRoster(); break;
            case Page.ManageVipMemberDetail:
                DrawManageVipMemberDetail(); break;
            case Page.ManageVipAssignTarget:
                DrawManageVipAssignTarget(); break;
            case Page.ManageGiveawayList:
                DrawManageGiveawayList(); break;
            case Page.ManageGiveawayDetail:
                DrawManageGiveawayDetail(); break;
            case Page.ManageRaffleList:
                DrawManageRaffleList(); break;
            case Page.ManageRaffleDetail:
                DrawManageRaffleDetail(); break;
            case Page.ManageRaffleAssignTarget:
                DrawManageRaffleAssignTarget(); break;
            case Page.ManageStaffingRoster:
                DrawManageStaffingRoster(); break;
            case Page.ManageStaffingMemberDetail:
                DrawManageStaffingMemberDetail(); break;
            case Page.ManageStaffingHireTarget:
                DrawManageStaffingHireTarget(); break;
            case Page.ManageEventList:
                DrawManageEventList(); break;
            case Page.ManageEventDetail:
                DrawManageEventDetail(); break;
            case Page.ManageEventAssignSignupTarget:
                DrawManageEventAssignSignupTarget(); break;
            default:
                DrawHome(); break;
        }
    }

    // Static, server-driven menu - no drag/reorder, so a fixed grid is all that's needed here
    // (unlike a genuine customizable-home-screen plugin, which would need real bin-packing/
    // drag-state machinery for this same visual shape). See MainWindow.Motion.cs's attribution
    // note - the icon-tile-grid concept was inspired by, not copied from, another plugin's home
    // screen; this grid and DrawHomeTile below are an original implementation.
    private static readonly (string Id, FontAwesomeIcon Icon, string Label)[] HomeTiles =
    {
        ("hometile:vip", FontAwesomeIcon.Crown, "VIP Status"),
        ("hometile:events", FontAwesomeIcon.Calendar, "Events"),
        ("hometile:profiles", FontAwesomeIcon.IdCard, "Profiles"),
        ("hometile:giveaways", FontAwesomeIcon.Gift, "Giveaways"),
        ("hometile:raffles", FontAwesomeIcon.Ticket, "Raffles"),
        ("hometile:manage", FontAwesomeIcon.Cogs, "Manage"),
    };

    private void DrawHome()
    {
        var label = plugin.Configuration.LinkedDiscordUsername
            ?? plugin.Configuration.LinkedDiscordUserId?.ToString()
            ?? "Unknown";
        ImGui.TextDisabled($"Linked as {label}");
        ImGui.Spacing();
        ImGui.Spacing();

        const int columns = 3;
        var avail = ImGui.GetContentRegionAvail().X;
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        var tileSize = Math.Clamp((avail - spacing * (columns - 1)) / columns, 60f, 160f);

        for (var i = 0; i < HomeTiles.Length; i++)
        {
            var (id, icon, tileLabel) = HomeTiles[i];
            if (DrawHomeTile(id, icon, tileLabel, tileSize))
            {
                switch (id)
                {
                    case "hometile:vip": StartVipStatus(); break;
                    case "hometile:events": StartEvents(); break;
                    case "hometile:profiles": StartProfiles(); break;
                    case "hometile:giveaways": StartGiveaways(); break;
                    case "hometile:raffles": StartRaffles(); break;
                    case "hometile:manage": StartManage(); break;
                }
            }
            if (i % columns != columns - 1 && i != HomeTiles.Length - 1)
                ImGui.SameLine();
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        if (ColoredButton("Settings", MutedColor, FullWidthButton))
            StartSettings();
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

    // Reserve (InvisibleButton) -> query (IsItemHovered/Active, GetItemRectMin/Max) -> draw, all
    // before any other widget is submitted - the correct order for this immediate-mode binding,
    // so the hit-test lines up with what's actually drawn this frame. Wrapped in BeginGroup/
    // EndGroup so the tile + its label below count as one item for the caller's SameLine() calls
    // - without the group, the label's own cursor placement would break the grid's row alignment
    // on the next tile. Only the drawn rect is scaled by the hover/press spring, never the
    // InvisibleButton's own hitbox, so hovering can't grow the tile into its own hitbox and
    // create a self-reinforcing flicker loop at the edge.
    private static bool DrawHomeTile(string id, FontAwesomeIcon icon, string label, float tileSize)
    {
        ImGui.BeginGroup();
        var clicked = ImGui.InvisibleButton($"##{id}", new Vector2(tileSize, tileSize));
        var hovered = ImGui.IsItemHovered();
        var active = ImGui.IsItemActive();
        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();

        var target = active ? 0.94f : hovered ? 1.04f : 1f;
        var scale = StepSpring(id, target, 0.1f);
        var center = (min + max) / 2f;
        var half = (max - min) / 2f * scale;

        var bgColor = hovered ? Brighten(AccentColor) : WithAlpha(AccentColor, 0.15f);
        var drawList = ImGui.GetWindowDrawList();
        drawList.AddRectFilled(center - half, center + half, ImGui.GetColorU32(bgColor), 8f, ImDrawFlags.RoundCornersAll);

        var iconFont = Plugin.PluginInterface.UiBuilder.FontIcon;
        var iconText = icon.ToIconString();
        ImGui.PushFont(iconFont);
        var iconSize = ImGui.CalcTextSize(iconText, false, -1f);
        ImGui.PopFont();
        var iconPos = new Vector2(center.X - iconSize.X / 2f, min.Y + tileSize * 0.28f);
        drawList.AddText(iconFont, iconFont.FontSize, iconPos, ImGui.GetColorU32(AccentColor), iconText, 0f);

        var labelSize = ImGui.CalcTextSize(label, false, -1f);
        ImGui.SetCursorScreenPos(new Vector2(min.X + (tileSize - labelSize.X) / 2f, max.Y + 4f));
        ImGui.TextUnformatted(label);
        ImGui.EndGroup();

        return clicked;
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

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        if (ColoredButton("Settings", MutedColor))
            StartSettings();
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
