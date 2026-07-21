using System;
using System.Numerics;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Utility;
using FroggePlugin.Api;

namespace FroggePlugin.Windows;

public partial class MainWindow
{
    // --- Style toolkit -----------------------------------------------------------------
    // A small, shared set of colors/helpers so every screen looks like one app instead of
    // a pile of default-styled ImGui widgets. Deliberately conservative about which ImGui
    // primitives it leans on (PushStyleColor/PopStyleColor/TextDisabled/sized Button) since
    // there's no way to visually iterate against a live render from here - every one of
    // these is confirmed by a successful build, not just assumed.

    private static readonly Vector4 AccentColor = new(0.40f, 0.82f, 0.55f, 1.00f);
    private static readonly Vector4 SuccessColor = new(0.40f, 0.82f, 0.55f, 1.00f);
    private static readonly Vector4 WarningColor = new(0.95f, 0.75f, 0.20f, 1.00f);
    private static readonly Vector4 DangerColor = new(0.92f, 0.38f, 0.38f, 1.00f);
    private static readonly Vector4 MutedColor = new(0.63f, 0.63f, 0.66f, 1.00f);

    private static readonly Vector2 FullWidthButton = new(-1, 0);

    private static void DrawTitle(string text)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, AccentColor);
        ImGui.TextWrapped(text);
        ImGui.PopStyleColor();
    }

    private static void DrawColored(string text, Vector4 color)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, color);
        ImGui.TextWrapped(text);
        ImGui.PopStyleColor();
    }

    private static Vector4 Brighten(Vector4 color, float factor = 1.15f) =>
        new(Math.Min(color.X * factor, 1f), Math.Min(color.Y * factor, 1f), Math.Min(color.Z * factor, 1f), color.W);

    private static void PushButtonColor(Vector4 color)
    {
        ImGui.PushStyleColor(ImGuiCol.Button, color);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, Brighten(color));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, Brighten(color, 0.85f));
    }

    private static void PopButtonColor() => ImGui.PopStyleColor(3);

    private static bool ColoredButton(string label, Vector4 color, Vector2 size = default)
    {
        PushButtonColor(color);
        var clicked = ImGui.Button(label, size);
        PopButtonColor();
        return clicked;
    }

    private static bool DrawBackButton()
    {
        return ColoredButton("< Back", MutedColor);
    }

    // The API returns plain https://discord.com/... links (general-purpose, works as a browser
    // fallback too). Rewriting to discord:// here - not server-side - is deliberate: Discord's
    // desktop client registers discord:// as a real OS-level URL protocol handler (confirmed via
    // the Windows registry, HKEY_CLASSES_ROOT\discord), so this makes Util.OpenLink launch the
    // client directly instead of falling through to whatever handles https (typically a
    // browser) - a Plugin/OS-environment concern, not something the API's link-resolution logic
    // should need to encode.
    private static void OpenDiscordLink(string url) =>
        Util.OpenLink(url.Replace("https://discord.com", "discord://discord.com"));

    private static void DrawLoading() => ImGui.TextDisabled("Loading...");

    private static void DrawError(string? message, Action retry)
    {
        DrawColored(message ?? "Something went wrong.", DangerColor);
        if (ImGui.Button("Retry"))
        {
            retry();
        }
    }

    private static void DrawEmpty(string message) => ImGui.TextDisabled(message);

    // Shared by DrawGiveaways()/DrawRaffles() - both screens are just "pick a linked venue,
    // then go somewhere feature-specific," backed by the same guildsLoadState/guilds fields
    // (MainWindow.cs) since venue membership has nothing to do with which feature is asking.
    private void DrawGuildPicker(Action retry, Action<ulong, string> onSelect)
    {
        if (DrawBackButton())
        {
            page = Page.Home;
            guildsLoadState = VipLoadState.Idle;
            guilds = null;
            guildsErrorMessage = null;
            return;
        }

        ImGui.Separator();
        ImGui.Spacing();

        switch (guildsLoadState)
        {
            case VipLoadState.Loading:
                DrawLoading();
                break;

            case VipLoadState.Error:
                DrawError(guildsErrorMessage, retry);
                break;

            case VipLoadState.Loaded:
                if (guilds is null || guilds.Count == 0)
                {
                    DrawEmpty("No linked venues yet. Run /plugin-link again if you've joined a new one.");
                    break;
                }

                foreach (var guild in guilds)
                {
                    if (ColoredButton($"{guild.GuildName}##{guild.GuildId}", AccentColor, FullWidthButton))
                        onSelect(guild.GuildId, guild.GuildName);
                    ImGui.Spacing();
                }
                break;
        }
    }

    // Renders a VIP tier pick-list, backed by the shared vipTiers/vipTiersLoadState fields
    // (MainWindow.ManageVip.cs) - reused by both the member-detail "change tier" flow and the
    // assign-via-target flow, matching DrawGuildPicker's "shared data, per-caller onSelect"
    // shape. No Back button here (unlike DrawGuildPicker) - this is an inline picker within a
    // larger screen, not its own Page.
    private void DrawTierPicker(Action retry, Action<int, string> onSelect)
    {
        switch (vipTiersLoadState)
        {
            case VipLoadState.Loading:
                DrawLoading();
                break;

            case VipLoadState.Error:
                DrawError(vipTiersErrorMessage, retry);
                break;

            case VipLoadState.Loaded:
                if (vipTiers is null || vipTiers.Count == 0)
                {
                    DrawEmpty("No VIP tiers configured for this venue yet.");
                    break;
                }

                foreach (var tier in vipTiers)
                {
                    if (ColoredButton($"{tier.Name} ({tier.Cost:N0})##{tier.Id}", AccentColor, FullWidthButton))
                        onSelect(tier.Id, tier.Name);
                    ImGui.Spacing();
                }
                break;
        }
    }

    // Shared by every Fetch*Async method across Vip/Events/Profiles/Giveaways/Raffles.cs - each
    // used to hand-roll the same try/fetch/null-check/catch shape against its own VipLoadState
    // field. `ref`/`out` parameters aren't allowed in async methods, so the per-call state field
    // can't be passed by reference - callers supply a `setState` closure instead. Not used by
    // PerformShiftActionAsync (Events.cs, a genuinely different shape: two sequential awaits with
    // a staleness guard after each) or LinkAsync (MainWindow.cs, its own distinct LinkState enum).
    private static async Task LoadAsync<T>(
        Func<Task<T?>> fetch,
        Action<T> onSuccess,
        Action<VipLoadState, string?> setState,
        string notFoundMessage) where T : class
    {
        try
        {
            var result = await fetch();
            if (result is null)
            {
                setState(VipLoadState.Error, notFoundMessage);
                return;
            }
            onSuccess(result);
            setState(VipLoadState.Loaded, null);
        }
        catch (Exception ex)
        {
            setState(VipLoadState.Error, $"{notFoundMessage}: {ex.Message}");
        }
    }

    // A bordered "card" for list items. Deliberately not BeginChild/EndChild: reflecting on the
    // actual Dalamud.Bindings.ImGui.dll on disk (not just general ImGui knowledge) showed this
    // binding has no ImGuiChildFlags at all - it's the older bool-border BeginChild overload,
    // where a size.Y of 0 means "fill all remaining space in the window," not "auto-fit content."
    // Using that here would make every card in a list eat the rest of the window. Instead this
    // groups the content (BeginGroup/EndGroup - pure layout, no child-window sizing semantics)
    // and draws a border around its resulting bounding box after the fact, with a manually
    // tracked width so the box spans the full row instead of hugging the longest line of text.
    private float cardContentWidth;

    private void BeginCard()
    {
        cardContentWidth = ImGui.GetContentRegionAvail().X;
        ImGui.BeginGroup();
        ImGui.Dummy(new Vector2(0, 4));
        ImGui.Indent();
    }

    private void EndCard(Vector4? borderColor = null)
    {
        ImGui.Unindent();
        ImGui.Dummy(new Vector2(0, 4));
        ImGui.EndGroup();

        var min = ImGui.GetItemRectMin();
        var max = new Vector2(min.X + cardContentWidth, ImGui.GetItemRectMax().Y);
        ImGui.GetWindowDrawList().AddRect(min, max, ImGui.GetColorU32(borderColor ?? MutedColor), 4f);
        ImGui.Spacing();
    }

    private static Vector4 ApprovalStatusColor(string status) => status switch
    {
        "Approved" => SuccessColor,
        "Pending Approval" => WarningColor,
        "Rejected" => DangerColor,
        _ => MutedColor,
    };

    // A small colored, non-interactive "chip" - reuses the already-proven Button/BeginDisabled
    // styling machinery rather than drawing raw rectangles via the draw list, since the exact
    // draw-list API shape in this binding hasn't been confirmed against a real render.
    private static void DrawBadge(string text, Vector4 color)
    {
        PushButtonColor(color);
        ImGui.BeginDisabled(true);
        ImGui.Button(text);
        ImGui.EndDisabled();
        PopButtonColor();
    }

    // A small-caps, muted sub-header for grouping a screen into sections (distinct from
    // DrawTitle's accent-colored top-level titles).
    private static void DrawSectionHeader(string text)
    {
        ImGui.Spacing();
        ImGui.TextDisabled(text.ToUpperInvariant());
        ImGui.Separator();
        ImGui.Spacing();
    }

    // A two-column "label: value" row inside a BeginTable(id, 2, ...) block - skips entirely
    // (no row at all) if both fields are empty, so sparse optional data doesn't leave blank rows.
    private static void DrawFieldRow(string label1, string? value1, string label2, string? value2)
    {
        if (string.IsNullOrEmpty(value1) && string.IsNullOrEmpty(value2))
            return;
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        if (!string.IsNullOrEmpty(value1))
        {
            ImGui.TextDisabled($"{label1}:");
            ImGui.TextWrapped(value1);
        }
        ImGui.TableNextColumn();
        if (!string.IsNullOrEmpty(value2))
        {
            ImGui.TextDisabled($"{label2}:");
            ImGui.TextWrapped(value2);
        }
    }

    private static Vector4 ExpiryColor(DateTimeOffset? expiresAt)
    {
        if (expiresAt is not { } expiry)
            return SuccessColor;
        var daysRemaining = (expiry - DateTimeOffset.Now).TotalDays;
        return daysRemaining switch
        {
            < 7 => DangerColor,
            < 30 => WarningColor,
            _ => MutedColor,
        };
    }

    private static void DrawInlineField(string label, string? value)
    {
        if (string.IsNullOrEmpty(value))
            return;
        ImGui.TextDisabled($"{label}:");
        ImGui.SameLine();
        ImGui.TextWrapped(value);
    }

    private static void DrawLongField(string label, string? value)
    {
        if (string.IsNullOrEmpty(value))
            return;
        ImGui.TextDisabled(label);
        ImGui.TextWrapped(value);
        ImGui.Spacing();
    }

    // The read-only character-content body shared by the owner's self-view (MainWindow.Profiles.cs)
    // and the manager's review screen (MainWindow.Manage.cs) - callers wrap this in their own
    // BeginCard()/EndCard(), and the manager screen appends Approve/Reject buttons after calling
    // this, before its own EndCard(). Justified as a shared static helper now that there's a
    // second real consumer, matching this codebase's established "second consumer justifies
    // extraction" precedent.
    private static void DrawProfileContent(PluginProfileDetail p)
    {
        var statusColor = ApprovalStatusColor(p.ApprovalStatus);

        DrawTitle(p.CharacterName);
        if (p.IsPrimary)
        {
            ImGui.SameLine();
            DrawBadge("Primary", AccentColor);
        }
        ImGui.SameLine();
        DrawBadge(p.ApprovalStatus, statusColor);
        ImGui.TextDisabled(p.GuildName);
        if (p.RejectionReason is not null)
        {
            ImGui.Spacing();
            DrawColored($"Rejection reason: {p.RejectionReason}", DangerColor);
        }

        var hasMainInfo = !string.IsNullOrEmpty(p.Jobs) || !string.IsNullOrEmpty(p.Rates);
        if (hasMainInfo)
        {
            DrawSectionHeader("Main Info");
            if (ImGui.BeginTable("##maininfo", 2, ImGuiTableFlags.SizingStretchSame))
            {
                DrawFieldRow("Jobs", p.Jobs, "Rates", p.Rates);
                ImGui.EndTable();
            }
        }

        var hasGlance = !string.IsNullOrEmpty(p.Race) || !string.IsNullOrEmpty(p.Clan)
            || !string.IsNullOrEmpty(p.Gender) || !string.IsNullOrEmpty(p.Pronouns)
            || !string.IsNullOrEmpty(p.Orientation) || !string.IsNullOrEmpty(p.World)
            || !string.IsNullOrEmpty(p.DataCenter) || !string.IsNullOrEmpty(p.Height)
            || !string.IsNullOrEmpty(p.Age) || !string.IsNullOrEmpty(p.MareCode);
        if (hasGlance)
        {
            DrawSectionHeader("At A Glance");
            if (ImGui.BeginTable("##glance", 2, ImGuiTableFlags.SizingStretchSame))
            {
                DrawFieldRow("Race", p.Race, "Clan", p.Clan);
                DrawFieldRow("Gender", p.Gender, "Pronouns", p.Pronouns);
                DrawFieldRow("Orientation", p.Orientation, "World", p.World);
                DrawFieldRow("Data Center", p.DataCenter, "Height", p.Height);
                DrawFieldRow("Age", p.Age, "Mare Code", p.MareCode);
                ImGui.EndTable();
            }
        }

        var hasNarrativeFields = !string.IsNullOrEmpty(p.Likes) || !string.IsNullOrEmpty(p.Dislikes)
            || !string.IsNullOrEmpty(p.Personality) || !string.IsNullOrEmpty(p.AboutMe);
        if (hasNarrativeFields)
        {
            DrawSectionHeader("Personality");
            DrawLongField("Likes", p.Likes);
            DrawLongField("Dislikes", p.Dislikes);
            DrawLongField("Personality", p.Personality);
            DrawLongField("About Me", p.AboutMe);
        }

        if (p.ThumbnailUrl is not null || p.MainImageUrl is not null || p.AdditionalImages.Count > 0)
        {
            // Actual image rendering (fetching + texture-uploading a remote URL) is a
            // real separate capability this plugin doesn't have yet - deliberately out
            // of scope for a styling pass, same as event image_url. URLs stay as text.
            DrawSectionHeader("Images");
            if (p.ThumbnailUrl is not null)
                DrawInlineField("Thumbnail", p.ThumbnailUrl);
            if (p.MainImageUrl is not null)
                DrawInlineField("Main Image", p.MainImageUrl);
            foreach (var image in p.AdditionalImages)
                DrawInlineField(image.Caption ?? "Image", image.ImageUrl);
        }
    }
}
