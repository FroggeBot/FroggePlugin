using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Objects.SubKinds;
using FroggePlugin.Api;

namespace FroggePlugin.Windows;

public partial class MainWindow
{
    private bool showConcludedManageRaffles;
    private VipLoadState manageRaffleListLoadState = VipLoadState.Idle;
    private string? manageRaffleListErrorMessage;
    private List<PluginManageRaffleSummary>? manageRaffles;

    // Same "list already carries the detail" design as Giveaways' selectedManageGiveaway.
    private PluginManageRaffleSummary? selectedManageRaffle;

    // Guards a roll/credit-tickets completion against firing after the user has navigated away -
    // shared by both actions on this one detail screen, matching ManageVip.cs's vipActionSequence
    // covering both Change Tier and Remove.
    private int raffleActionSequence;
    private bool raffleActionInProgress;
    private string? raffleActionErrorMessage;

    private string ticketQuantityInput = string.Empty;

    private void DrawManageRaffleList()
    {
        if (DrawBackButton())
        {
            page = Page.ManageVenue;
            manageRaffleListLoadState = VipLoadState.Idle;
            manageRaffles = null;
            manageRaffleListErrorMessage = null;
            return;
        }

        ImGui.Separator();
        ImGui.Spacing();
        DrawTitle($"{selectedManageGuildName} - Raffles");
        ImGui.Spacing();

        DrawFilterTabs("Open", "Concluded", showConcludedManageRaffles, showSecond =>
        {
            showConcludedManageRaffles = showSecond;
            StartManageRaffleFetch();
        });
        ImGui.Spacing();

        switch (manageRaffleListLoadState)
        {
            case VipLoadState.Loading:
                DrawLoading();
                break;

            case VipLoadState.Error:
                DrawError(manageRaffleListErrorMessage, StartManageRaffleFetch);
                break;

            case VipLoadState.Loaded:
                if (manageRaffles is null || manageRaffles.Count == 0)
                {
                    DrawEmpty(showConcludedManageRaffles ? "No concluded raffles yet." : "No open raffles.");
                    break;
                }

                foreach (var raffle in manageRaffles)
                {
                    BeginCard();
                    DrawTitle(raffle.Name ?? "(unnamed raffle)");
                    ImGui.TextDisabled($"Cost/Ticket: {raffle.CostPerTicket:N0} · Split: {raffle.WinnerPct}%");
                    ImGui.TextDisabled($"{raffle.EntrantCount} entrant(s) · {raffle.TotalTickets} ticket(s) in the pot");
                    ImGui.Spacing();
                    if (ImGui.Button($"Manage##{raffle.Id}"))
                        StartManageRaffleDetail(raffle);
                    EndCard(AccentColor);
                }
                break;
        }
    }

    private void StartManageRaffleList()
    {
        page = Page.ManageRaffleList;
        showConcludedManageRaffles = false;
        StartManageRaffleFetch();
    }

    private void StartManageRaffleFetch()
    {
        manageRaffleListLoadState = VipLoadState.Loading;
        manageRaffleListErrorMessage = null;
        _ = FetchManageRafflesAsync();
    }

    private Task FetchManageRafflesAsync() => LoadAsync(
        () => showConcludedManageRaffles
            ? plugin.ApiClient.GetManageRafflesConcludedAsync(selectedManageGuildId)
            : plugin.ApiClient.GetManageRafflesAsync(selectedManageGuildId),
        result => manageRaffles = result,
        (loadState, err) => { manageRaffleListLoadState = loadState; if (err != null) manageRaffleListErrorMessage = err; },
        "Couldn't load raffles");

    private void DrawManageRaffleDetail()
    {
        if (DrawBackButton())
        {
            page = Page.ManageRaffleList;
            selectedManageRaffle = null;
            raffleActionInProgress = false;
            raffleActionErrorMessage = null;
            raffleActionSequence++;
            return;
        }

        ImGui.Separator();
        ImGui.Spacing();

        if (selectedManageRaffle is not { } raffle)
            return;

        BeginCard();
        DrawTitle(raffle.Name ?? "(unnamed raffle)");
        ImGui.Spacing();
        DrawInlineField("Cost/Ticket", $"{raffle.CostPerTicket:N0}");
        DrawInlineField("Split", $"{raffle.WinnerPct}%");
        DrawInlineField("Entrants", raffle.EntrantCount.ToString());
        DrawInlineField("Total Tickets", raffle.TotalTickets.ToString());
        if (raffle.IsRolled)
        {
            DrawInlineField("Rolled", raffle.RolledAt is { } rolledAt ? $"{rolledAt.LocalDateTime:g}" : "Yes");
            if (raffle.WinnerIds.Count > 0)
            {
                ImGui.Spacing();
                DrawSectionHeader("Winners");
                foreach (var winnerId in raffle.WinnerIds)
                    DrawColored($"• {winnerId}", SuccessColor);
            }
        }
        EndCard(AccentColor);

        ImGui.Spacing();
        if (raffleActionErrorMessage is not null)
            DrawColored(raffleActionErrorMessage, DangerColor);

        ImGui.BeginDisabled(raffleActionInProgress || raffle.IsRolled);
        if (ColoredButton("Add Tickets", AccentColor))
            StartManageRaffleAssignTarget();
        ImGui.EndDisabled();

        ImGui.Spacing();
        if (raffle.IsRolled)
            DrawColored("Already rolled - clicking again will pick a fresh set of winners.", WarningColor);

        ImGui.BeginDisabled(raffleActionInProgress);
        if (ColoredButton(raffle.IsRolled ? "Re-Roll Winners" : "Roll Winners", raffle.IsRolled ? WarningColor : SuccessColor))
            StartRollRaffle();
        ImGui.EndDisabled();
    }

    private void StartManageRaffleDetail(PluginManageRaffleSummary raffle)
    {
        page = Page.ManageRaffleDetail;
        selectedManageRaffle = raffle;
        raffleActionInProgress = false;
        raffleActionErrorMessage = null;
        raffleActionSequence++;
    }

    private void StartRollRaffle()
    {
        if (selectedManageRaffle is not { } raffle)
            return;
        raffleActionSequence++;
        var mySequence = raffleActionSequence;
        raffleActionInProgress = true;
        raffleActionErrorMessage = null;
        _ = PerformRollRaffleAsync(mySequence, raffle.Id, raffle.IsRolled);
    }

    private async Task PerformRollRaffleAsync(int mySequence, int raffleId, bool force)
    {
        try
        {
            var result = await plugin.ApiClient.RollRaffleAsync(selectedManageGuildId, raffleId, force);
            if (mySequence != raffleActionSequence)
                return; // navigated away - this completion is abandoned, don't touch shared state

            if (result is null)
            {
                raffleActionErrorMessage = "That action failed. Try again.";
                raffleActionInProgress = false;
                return;
            }

            raffleActionInProgress = false;
            selectedManageRaffle = result;
        }
        catch (Exception)
        {
            if (mySequence == raffleActionSequence)
            {
                raffleActionErrorMessage = "That action failed. Try again.";
                raffleActionInProgress = false;
            }
        }
    }

    // Target-resolve reuses the shared resolveState/resolvedDiscordUserId/resolvedCharacterName
    // fields (declared in MainWindow.ManageVip.cs) - this is their second real consumer, matching
    // this codebase's "promote once a second consumer needs it" rule already applied to the
    // guild-picker state. Only the quantity-input step below is Raffle-specific, since a ticket
    // count is arbitrary free-form input rather than a fixed pick-list like VIP's tier.
    private void DrawManageRaffleAssignTarget()
    {
        if (DrawBackButton())
        {
            page = Page.ManageRaffleDetail;
            resolveState = VipLoadState.Idle;
            resolveErrorMessage = null;
            resolvedDiscordUserId = null;
            resolvedCharacterName = null;
            ticketQuantityInput = string.Empty;
            raffleActionInProgress = false;
            raffleActionErrorMessage = null;
            raffleActionSequence++;
            return;
        }

        ImGui.Separator();
        ImGui.Spacing();
        DrawTitle("Add Tickets");
        ImGui.Spacing();
        ImGui.TextWrapped(
            "Target a player character in-game, then use their currently-targeted character to " +
            "look up the Discord account it's linked to via /verify."
        );
        ImGui.Spacing();

        var resolving = resolveState == VipLoadState.Loading;
        ImGui.BeginDisabled(resolving);
        if (ColoredButton("Use Current Target", AccentColor))
            StartResolveRaffleTarget();
        ImGui.EndDisabled();

        ImGui.Spacing();
        switch (resolveState)
        {
            case VipLoadState.Loading:
                DrawLoading();
                break;

            case VipLoadState.Error:
                DrawColored(resolveErrorMessage ?? "Couldn't resolve that character.", DangerColor);
                break;

            case VipLoadState.Loaded:
                if (resolvedDiscordUserId is not { } discordUserId)
                    break;

                DrawColored($"Found: {resolvedCharacterName}", SuccessColor);
                ImGui.Spacing();

                if (raffleActionErrorMessage is not null)
                    DrawColored(raffleActionErrorMessage, DangerColor);

                ImGui.SetNextItemWidth(-1);
                ImGui.InputText("##TicketQuantity", ref ticketQuantityInput, 10, ImGuiInputTextFlags.CharsDecimal);
                ImGui.Spacing();

                var validQuantity = int.TryParse(ticketQuantityInput, out var quantity) && quantity > 0;
                ImGui.BeginDisabled(raffleActionInProgress || !validQuantity);
                if (ColoredButton("Credit Tickets", SuccessColor, FullWidthButton))
                    StartCreditTickets(discordUserId, quantity);
                ImGui.EndDisabled();
                break;
        }
    }

    private void StartManageRaffleAssignTarget()
    {
        page = Page.ManageRaffleAssignTarget;
        resolveState = VipLoadState.Idle;
        resolveErrorMessage = null;
        resolvedDiscordUserId = null;
        resolvedCharacterName = null;
        ticketQuantityInput = string.Empty;
        raffleActionInProgress = false;
        raffleActionErrorMessage = null;
    }

    private void StartResolveRaffleTarget()
    {
        resolveState = VipLoadState.Loading;
        resolveErrorMessage = null;
        resolvedDiscordUserId = null;
        _ = ResolveRaffleTargetAsync();
    }

    // Reads the current target BEFORE the first await, deliberately - see ManageVip.cs's
    // ResolveTargetAsync for the full explanation of why this is thread-safe.
    private async Task ResolveRaffleTargetAsync()
    {
        try
        {
            if (Plugin.TargetManager.Target is not IPlayerCharacter player)
            {
                resolveState = VipLoadState.Error;
                resolveErrorMessage = "Target a player character first.";
                return;
            }

            var characterName = player.Name.TextValue;
            var world = player.HomeWorld.ValueNullable?.Name.ExtractText();
            if (string.IsNullOrEmpty(characterName) || string.IsNullOrEmpty(world))
            {
                resolveState = VipLoadState.Error;
                resolveErrorMessage = "Couldn't read that character's name or world.";
                return;
            }

            var result = await plugin.ApiClient.ResolveCharacterAsync(selectedManageGuildId, characterName, world);
            if (result is null)
            {
                resolveState = VipLoadState.Error;
                resolveErrorMessage =
                    "No verified owner found for this character in this venue. They may need to run " +
                    "/verify in Discord first, or you can credit them from there instead.";
                return;
            }

            resolvedDiscordUserId = result.DiscordUserId;
            resolvedCharacterName = result.CharacterName;
            resolveState = VipLoadState.Loaded;
        }
        catch (Exception ex)
        {
            resolveState = VipLoadState.Error;
            resolveErrorMessage = $"Couldn't resolve target: {ex.Message}";
        }
    }

    private void StartCreditTickets(ulong discordUserId, int quantity)
    {
        raffleActionSequence++;
        var mySequence = raffleActionSequence;
        raffleActionInProgress = true;
        raffleActionErrorMessage = null;
        _ = PerformCreditTicketsAsync(mySequence, discordUserId, quantity);
    }

    private async Task PerformCreditTicketsAsync(int mySequence, ulong discordUserId, int quantity)
    {
        try
        {
            if (selectedManageRaffle is not { } raffle)
                return;

            var result = await plugin.ApiClient.CreditRaffleTicketsAsync(selectedManageGuildId, raffle.Id, discordUserId, quantity);
            if (mySequence != raffleActionSequence)
                return; // navigated away - this completion is abandoned, don't touch shared state

            if (result is null)
            {
                raffleActionErrorMessage = "That action failed. Try again.";
                raffleActionInProgress = false;
                return;
            }

            // Ticket count is now stale for the roster/detail either way - land back on the detail
            // screen showing the freshly updated totals.
            raffleActionInProgress = false;
            selectedManageRaffle = result;
            page = Page.ManageRaffleDetail;
        }
        catch (Exception)
        {
            if (mySequence == raffleActionSequence)
            {
                raffleActionErrorMessage = "That action failed. Try again.";
                raffleActionInProgress = false;
            }
        }
    }
}
