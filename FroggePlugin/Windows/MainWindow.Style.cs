using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;

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
}
