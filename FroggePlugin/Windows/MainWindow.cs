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
        AttendingVenues,
        WorkAtVenue,
        VipStatus,
        VipHistory,
        VipPerks,
        Events,
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
            case Page.AttendingVenues:
                DrawAttendingVenues(); break;
            case Page.WorkAtVenue:
                DrawWorkAtVenue(); break;
            case Page.VipStatus:
                DrawVipStatus(); break;
            case Page.VipHistory:
                DrawVipHistory(); break;
            case Page.VipPerks:
                DrawVipPerks(); break;
            case Page.Events:
                DrawEvents(); break;
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

    // Root "what do you want to do" menu - a role choice, not a feature list. Each option leads
    // to its own sub-menu (Manage -> the existing venue-manager guild picker; Attending -> the
    // former flat feature grid, now one level deeper; Work -> not built yet, see DrawWorkAtVenue).
    private static readonly (string Id, FontAwesomeIcon Icon, string Label)[] RootTiles =
    {
        ("roottile:manage", FontAwesomeIcon.Cogs, "Venue Manager"),
        ("roottile:work", FontAwesomeIcon.Briefcase, "Venue Employee"),
        ("roottile:attend", FontAwesomeIcon.Users, "Venue Attendee"),
    };

    // The member-facing feature set - formerly Home's own tile grid, demoted one level under
    // "Attending Venues" now that Home is a role-select screen rather than a flat feature list.
    private static readonly (string Id, FontAwesomeIcon Icon, string Label)[] AttendingTiles =
    {
        ("hometile:vip", FontAwesomeIcon.Crown, "VIP Status"),
        ("hometile:events", FontAwesomeIcon.Calendar, "Events"),
        ("hometile:profiles", FontAwesomeIcon.IdCard, "Profiles"),
        ("hometile:giveaways", FontAwesomeIcon.Gift, "Giveaways"),
        ("hometile:raffles", FontAwesomeIcon.Ticket, "Raffles"),
    };

    private void DrawHome()
    {
        var label = plugin.Configuration.LinkedDiscordUsername
            ?? plugin.Configuration.LinkedDiscordUserId?.ToString()
            ?? "Unknown";
        ImGui.TextDisabled($"Linked as {label}");

        // Right-justify Settings on the same line as the text above - GetContentRegionMax() is
        // already in the same window-local coordinate space SameLine's offset_from_start_x
        // expects, so no extra conversion is needed.
        const string settingsLabel = "Settings";
        var settingsWidth = ImGui.CalcTextSize(settingsLabel).X + ImGui.GetStyle().FramePadding.X * 2f;
        ImGui.SameLine(ImGui.GetContentRegionMax().X - settingsWidth);
        if (ColoredButton(settingsLabel, MutedColor))
            StartSettings();

        ImGui.Spacing();
        ImGui.Spacing();
        ImGui.TextDisabled("What role will you be playing tonight?");
        ImGui.Spacing();

        // Large squares in one horizontal row - exactly 3 role choices, one per column, no
        // wrapping logic needed the way the multi-row Attending Venues grid needs.
        const int columns = 3;
        var avail = ImGui.GetContentRegionAvail().X;
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        var tileSize = (avail - spacing * (columns - 1)) / columns;

        for (var i = 0; i < RootTiles.Length; i++)
        {
            var (id, icon, tileLabel) = RootTiles[i];
            if (DrawSquareTile(id, icon, tileLabel, tileSize))
            {
                switch (id)
                {
                    case "roottile:manage": StartManage(); break;
                    case "roottile:work": StartWorkAtVenue(); break;
                    case "roottile:attend": StartAttendingVenues(); break;
                }
            }
            if (i != RootTiles.Length - 1)
                ImGui.SameLine();
        }
    }

    // Icon centered above a centered label, both inside one square background - the root menu's
    // shape (3 large role-choice squares) reads better this way than DrawHomeTile's landscape
    // icon+label side by side, which is tuned for the narrower, denser Attending Venues grid.
    // Same reserve/query/draw order and hover/press spring as DrawHomeTile.
    private static bool DrawSquareTile(string id, FontAwesomeIcon icon, string label, float size)
    {
        ImGui.BeginGroup();
        var clicked = ImGui.InvisibleButton($"##{id}", new Vector2(size, size));
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
        var iconPos = new Vector2(center.X - iconSize.X / 2f, min.Y + size * 0.32f);
        drawList.AddText(iconFont, iconFont.FontSize, iconPos, ImGui.GetColorU32(AccentColor), iconText, 0f);

        var labelSize = ImGui.CalcTextSize(label, false, -1f);
        var labelPos = new Vector2(center.X - labelSize.X / 2f, min.Y + size * 0.68f);
        drawList.AddText(labelPos, ImGui.GetColorU32(ImGuiCol.Text), label);

        ImGui.EndGroup();
        return clicked;
    }

    private void StartAttendingVenues() => page = Page.AttendingVenues;

    private void DrawAttendingVenues()
    {
        if (DrawBackButton())
        {
            page = Page.Home;
            return;
        }

        ImGui.Separator();
        ImGui.Spacing();
        DrawTitle("Attending Venues");
        ImGui.Spacing();

        // Wider landscape tiles (icon + label side by side inside one box) read better as 2
        // columns than the old square grid's 3 - a 3rd column would either squeeze the label
        // text or force the tiles narrower than they need to be to hold it comfortably.
        const int columns = 2;
        const float tileHeight = 48f;
        var avail = ImGui.GetContentRegionAvail().X;
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        var tileWidth = (avail - spacing * (columns - 1)) / columns;
        var tileSize = new Vector2(tileWidth, tileHeight);

        for (var i = 0; i < AttendingTiles.Length; i++)
        {
            var (id, icon, tileLabel) = AttendingTiles[i];
            if (DrawHomeTile(id, icon, tileLabel, tileSize))
            {
                switch (id)
                {
                    case "hometile:vip": StartVipStatus(); break;
                    case "hometile:events": StartEvents(); break;
                    case "hometile:profiles": StartProfiles(); break;
                    case "hometile:giveaways": StartGiveaways(); break;
                    case "hometile:raffles": StartRaffles(); break;
                }
            }
            if (i % columns != columns - 1 && i != AttendingTiles.Length - 1)
                ImGui.SameLine();
        }
    }

    private void StartWorkAtVenue() => page = Page.WorkAtVenue;

    // Placeholder - no staff self-service screen exists in the plugin yet (only Discord's own
    // /staffing-status does today). This just gives the new role-select menu somewhere to land
    // for that option rather than leaving it unwired; the real screen is a separate, later round.
    private void DrawWorkAtVenue()
    {
        if (DrawBackButton())
        {
            page = Page.Home;
            return;
        }

        ImGui.Separator();
        ImGui.Spacing();
        DrawTitle("Work at a Venue");
        ImGui.Spacing();
        ImGui.TextWrapped("Staff self-service tools for the venues you work at are coming soon.");
    }

    // Reserve (InvisibleButton) -> query (IsItemHovered/Active, GetItemRectMin/Max) -> draw, all
    // before any other widget is submitted - the correct order for this immediate-mode binding,
    // so the hit-test lines up with what's actually drawn this frame. Icon and label are drawn
    // together inside the same landscape rect (icon on the left, label to its right, both
    // vertically centered) rather than the label sitting below a square icon box - no BeginGroup/
    // EndGroup wrapper needed here since everything (background, icon, label) is drawn via the
    // draw list against the one InvisibleButton's rect, not laid out as separate ImGui widgets.
    // Only the drawn rect is scaled by the hover/press spring, never the InvisibleButton's own
    // hitbox, so hovering can't grow the tile into its own hitbox and create a self-reinforcing
    // flicker loop at the edge.
    private static bool DrawHomeTile(string id, FontAwesomeIcon icon, string label, Vector2 size)
    {
        var clicked = ImGui.InvisibleButton($"##{id}", size);
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

        const float innerPadding = 12f;

        var iconFont = Plugin.PluginInterface.UiBuilder.FontIcon;
        var iconText = icon.ToIconString();
        ImGui.PushFont(iconFont);
        var iconSize = ImGui.CalcTextSize(iconText, false, -1f);
        ImGui.PopFont();
        var iconPos = new Vector2(min.X + innerPadding, center.Y - iconSize.Y / 2f);
        drawList.AddText(iconFont, iconFont.FontSize, iconPos, ImGui.GetColorU32(AccentColor), iconText, 0f);

        var labelSize = ImGui.CalcTextSize(label, false, -1f);
        var labelPos = new Vector2(iconPos.X + iconSize.X + innerPadding, center.Y - labelSize.Y / 2f);
        drawList.AddText(labelPos, ImGui.GetColorU32(ImGuiCol.Text), label);

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
