using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using FroggePlugin.Api;

namespace FroggePlugin.Windows;

public partial class MainWindow
{
    private bool showConcludedManageGiveaways;
    private VipLoadState manageGiveawayListLoadState = VipLoadState.Idle;
    private string? manageGiveawayListErrorMessage;
    private List<PluginManageGiveawaySummary>? manageGiveaways;

    // The list already carries everything the detail screen shows (matching the attendee-facing
    // Giveaways screen's own "no separate detail endpoint" design) - clicking into one just stores
    // the item directly rather than re-fetching it, and a successful roll replaces it in place.
    private PluginManageGiveawaySummary? selectedManageGiveaway;

    // Guards a roll completion against firing after the user has navigated away - same pattern as
    // Manage.cs's approvalActionSequence / ManageVip.cs's vipActionSequence.
    private int giveawayActionSequence;
    private bool giveawayActionInProgress;
    private string? giveawayActionErrorMessage;

    private void DrawManageGiveawayList()
    {
        if (DrawBackButton())
        {
            page = Page.ManageVenue;
            manageGiveawayListLoadState = VipLoadState.Idle;
            manageGiveaways = null;
            manageGiveawayListErrorMessage = null;
            return;
        }

        ImGui.Separator();
        ImGui.Spacing();
        DrawTitle($"{selectedManageGuildName} - Giveaways");
        ImGui.Spacing();

        if (ColoredButton("Open", showConcludedManageGiveaways ? MutedColor : AccentColor))
        {
            if (showConcludedManageGiveaways)
            {
                showConcludedManageGiveaways = false;
                StartManageGiveawayFetch();
            }
        }
        ImGui.SameLine();
        if (ColoredButton("Concluded", showConcludedManageGiveaways ? AccentColor : MutedColor))
        {
            if (!showConcludedManageGiveaways)
            {
                showConcludedManageGiveaways = true;
                StartManageGiveawayFetch();
            }
        }
        ImGui.Spacing();
        ImGui.Spacing();

        switch (manageGiveawayListLoadState)
        {
            case VipLoadState.Loading:
                DrawLoading();
                break;

            case VipLoadState.Error:
                DrawError(manageGiveawayListErrorMessage, StartManageGiveawayFetch);
                break;

            case VipLoadState.Loaded:
                if (manageGiveaways is null || manageGiveaways.Count == 0)
                {
                    DrawEmpty(showConcludedManageGiveaways ? "No concluded giveaways yet." : "No open giveaways.");
                    break;
                }

                foreach (var giveaway in manageGiveaways)
                {
                    BeginCard();
                    DrawTitle(giveaway.Name ?? "(unnamed giveaway)");
                    if (!string.IsNullOrEmpty(giveaway.Prize))
                        ImGui.TextWrapped(giveaway.Prize);
                    ImGui.TextDisabled($"{giveaway.EntrantCount} entrant(s)");
                    ImGui.Spacing();
                    if (ImGui.Button($"Manage##{giveaway.Id}"))
                        StartManageGiveawayDetail(giveaway);
                    EndCard(AccentColor);
                }
                break;
        }
    }

    private void StartManageGiveawayList()
    {
        page = Page.ManageGiveawayList;
        showConcludedManageGiveaways = false;
        StartManageGiveawayFetch();
    }

    private void StartManageGiveawayFetch()
    {
        manageGiveawayListLoadState = VipLoadState.Loading;
        manageGiveawayListErrorMessage = null;
        _ = FetchManageGiveawaysAsync();
    }

    private Task FetchManageGiveawaysAsync() => LoadAsync(
        () => showConcludedManageGiveaways
            ? plugin.ApiClient.GetManageGiveawaysConcludedAsync(selectedManageGuildId)
            : plugin.ApiClient.GetManageGiveawaysAsync(selectedManageGuildId),
        result => manageGiveaways = result,
        (loadState, err) => { manageGiveawayListLoadState = loadState; if (err != null) manageGiveawayListErrorMessage = err; },
        "Couldn't load giveaways");

    private void DrawManageGiveawayDetail()
    {
        if (DrawBackButton())
        {
            page = Page.ManageGiveawayList;
            selectedManageGiveaway = null;
            giveawayActionInProgress = false;
            giveawayActionErrorMessage = null;
            giveawayActionSequence++;
            return;
        }

        ImGui.Separator();
        ImGui.Spacing();

        if (selectedManageGiveaway is not { } giveaway)
            return;

        BeginCard();
        DrawTitle(giveaway.Name ?? "(unnamed giveaway)");
        if (!string.IsNullOrEmpty(giveaway.Prize))
            ImGui.TextWrapped(giveaway.Prize);
        ImGui.Spacing();
        DrawInlineField("Entrants", giveaway.EntrantCount.ToString());
        if (giveaway.IsRolled)
        {
            DrawInlineField("Rolled", giveaway.RolledAt is { } rolledAt ? $"{rolledAt.LocalDateTime:g}" : "Yes");
            if (giveaway.WinnerIds.Count > 0)
            {
                ImGui.Spacing();
                DrawSectionHeader("Winners");
                foreach (var winnerId in giveaway.WinnerIds)
                    ImGui.TextWrapped(winnerId.ToString());
            }
        }
        else
        {
            DrawInlineField("Ends", giveaway.EndAt is { } endAt ? $"{endAt.LocalDateTime:g}" : "No end date set");
        }
        EndCard(AccentColor);

        ImGui.Spacing();
        if (giveawayActionErrorMessage is not null)
            DrawColored(giveawayActionErrorMessage, DangerColor);

        if (giveaway.IsRolled)
            DrawColored("Already rolled - clicking again will pick a fresh set of winners.", WarningColor);

        ImGui.BeginDisabled(giveawayActionInProgress);
        if (ColoredButton(giveaway.IsRolled ? "Re-Roll Winners" : "Roll Winners", giveaway.IsRolled ? WarningColor : SuccessColor))
            StartRollGiveaway();
        ImGui.EndDisabled();
    }

    private void StartManageGiveawayDetail(PluginManageGiveawaySummary giveaway)
    {
        page = Page.ManageGiveawayDetail;
        selectedManageGiveaway = giveaway;
        giveawayActionInProgress = false;
        giveawayActionErrorMessage = null;
        giveawayActionSequence++;
    }

    private void StartRollGiveaway()
    {
        if (selectedManageGiveaway is not { } giveaway)
            return;
        giveawayActionSequence++;
        var mySequence = giveawayActionSequence;
        giveawayActionInProgress = true;
        giveawayActionErrorMessage = null;
        _ = PerformRollGiveawayAsync(mySequence, giveaway.Id, giveaway.IsRolled);
    }

    private async Task PerformRollGiveawayAsync(int mySequence, int giveawayId, bool force)
    {
        try
        {
            var result = await plugin.ApiClient.RollGiveawayAsync(selectedManageGuildId, giveawayId, force);
            if (mySequence != giveawayActionSequence)
                return; // navigated away - this completion is abandoned, don't touch shared state

            if (result is null)
            {
                giveawayActionErrorMessage = "That action failed. Try again.";
                giveawayActionInProgress = false;
                return;
            }

            giveawayActionInProgress = false;
            selectedManageGiveaway = result;
        }
        catch (Exception)
        {
            if (mySequence == giveawayActionSequence)
            {
                giveawayActionErrorMessage = "That action failed. Try again.";
                giveawayActionInProgress = false;
            }
        }
    }
}
