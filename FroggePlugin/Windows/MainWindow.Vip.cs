using System;
using System.Collections.Generic;
using System.Numerics;
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
            page = Page.AttendingVenues;
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

                    // Left: venue name/tier/expiry, in their own group so SameLine below can
                    // jump to the card's right edge regardless of how tall this block ends up.
                    ImGui.BeginGroup();
                    DrawTitle(membership.GuildName);
                    DrawBadge(membership.TierName, AccentColor);
                    if (membership.ExpiresAt is { } expiresAt)
                    {
                        // Days-remaining is already computed inside ExpiryColor for its color
                        // thresholds - surfacing the same number here too, since "8/26/2026"
                        // alone takes a beat to translate into "is that soon?" at a glance.
                        var daysRemaining = (expiresAt - DateTimeOffset.Now).TotalDays;
                        var relative = daysRemaining >= 0
                            ? $" ({Math.Ceiling(daysRemaining):0} day(s) left)"
                            : " (expired)";
                        DrawColored($"● Expires {expiresAt.LocalDateTime:d}{relative}", ExpiryColor(membership.ExpiresAt));
                    }
                    else
                    {
                        DrawColored("● Never expires", ExpiryColor(membership.ExpiresAt));
                    }
                    ImGui.EndGroup();

                    // Right: History/Perks stacked in a column flush against the card's right
                    // edge - cardContentWidth is measured from this same group's start point
                    // (BeginCard captures it before Indent()/BeginGroup()), matching the exact
                    // basis EndCard's own border math already uses, so this lines up with the
                    // card's true right edge rather than guessing at window-relative coordinates.
                    const float buttonWidth = 90f;
                    ImGui.SameLine(cardContentWidth - buttonWidth);
                    ImGui.BeginGroup();
                    if (ColoredButton($"History##{membership.GuildId}", AccentColor, new Vector2(buttonWidth, 0)))
                        StartVipHistory(membership.GuildId, membership.GuildName);
                    if (ColoredButton($"Perks##{membership.GuildId}", AccentColor, new Vector2(buttonWidth, 0)))
                        StartVipPerks(membership.GuildId, membership.GuildName);
                    ImGui.EndGroup();

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

    private Task FetchVipStatusAsync() => LoadAsync(
        plugin.ApiClient.GetVipMembershipsAsync,
        result => vipMemberships = result,
        (loadState, err) => { vipLoadState = loadState; if (err != null) vipErrorMessage = err; },
        "Couldn't load VIP status");

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

    private Task FetchVipHistoryAsync(ulong guildId) => LoadAsync(
        () => plugin.ApiClient.GetVipHistoryAsync(guildId),
        result => vipHistory = result,
        (loadState, err) => { historyLoadState = loadState; if (err != null) historyErrorMessage = err; },
        "Couldn't load VIP history");

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

    private Task FetchVipPerksAsync(ulong guildId) => LoadAsync(
        () => plugin.ApiClient.GetVipPerksAsync(guildId),
        result => vipPerks = result,
        (loadState, err) => { perksLoadState = loadState; if (err != null) perksErrorMessage = err; },
        "Couldn't load VIP perks");
}
