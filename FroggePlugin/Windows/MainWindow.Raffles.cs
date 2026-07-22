using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using FroggePlugin.Api;

namespace FroggePlugin.Windows;

public partial class MainWindow
{
    // Which venue's raffles are being browsed - set when a venue button is clicked in DrawRaffles.
    private ulong selectedRaffleGuildId;
    private string selectedRaffleGuildName = string.Empty;

    private bool showConcludedRaffles;
    private VipLoadState raffleListLoadState = VipLoadState.Idle;
    private string? raffleListErrorMessage;
    private List<PluginRaffleSummary>? raffles;

    private void DrawRaffles() => DrawGuildPicker(StartRaffles, StartRaffleList);

    private void StartRaffles()
    {
        page = Page.Raffles;
        guildsLoadState = VipLoadState.Loading;
        guildsErrorMessage = null;
        _ = FetchGuildsAsync();
    }

    private void DrawRaffleList()
    {
        if (DrawBackButton())
        {
            page = Page.Raffles;
            raffleListLoadState = VipLoadState.Idle;
            raffles = null;
            raffleListErrorMessage = null;
            return;
        }

        ImGui.Separator();
        ImGui.Spacing();
        DrawTitle(selectedRaffleGuildName);
        ImGui.Spacing();

        DrawFilterTabs("Open", "Concluded", showConcludedRaffles, showSecond =>
        {
            showConcludedRaffles = showSecond;
            StartRaffleFetch();
        });
        ImGui.Spacing();

        switch (raffleListLoadState)
        {
            case VipLoadState.Loading:
                DrawLoading();
                break;

            case VipLoadState.Error:
                DrawError(raffleListErrorMessage, StartRaffleFetch);
                break;

            case VipLoadState.Loaded:
                if (raffles is null || raffles.Count == 0)
                {
                    DrawEmpty(showConcludedRaffles ? "No concluded raffles yet." : "No open raffles.");
                    break;
                }

                foreach (var raffle in raffles)
                {
                    BeginCard();
                    DrawTitle(raffle.Name ?? "(unnamed raffle)");
                    ImGui.TextDisabled($"Cost/Ticket: {raffle.CostPerTicket:N0} · Split: {raffle.WinnerPct}%");

                    if (raffle.IsRolled)
                    {
                        ImGui.TextDisabled(raffle.RolledAt is { } rolledAt
                            ? $"Concluded {rolledAt.LocalDateTime:g}"
                            : "Concluded");
                    }

                    ImGui.TextDisabled($"{raffle.EntrantCount} entrant(s) · {raffle.TotalTickets} ticket(s) in the pot");

                    if (raffle.MyTicketCount > 0)
                        DrawColored($"Your Tickets: {raffle.MyTicketCount}", AccentColor);

                    if (raffle is { IsRolled: true, IsWinner: true })
                        DrawBadge("You Won!", SuccessColor);

                    if (raffle.DiscordLink is { } link)
                    {
                        ImGui.Spacing();
                        if (ImGui.Button($"View in Discord##{raffle.Id}"))
                            OpenDiscordLink(link);
                    }

                    EndCard(AccentColor);
                }
                break;
        }
    }

    private void StartRaffleList(ulong guildId, string guildName)
    {
        page = Page.RaffleList;
        selectedRaffleGuildId = guildId;
        selectedRaffleGuildName = guildName;
        showConcludedRaffles = false;
        StartRaffleFetch();
    }

    private void StartRaffleFetch()
    {
        raffleListLoadState = VipLoadState.Loading;
        raffleListErrorMessage = null;
        _ = FetchRafflesAsync();
    }

    private Task FetchRafflesAsync() => LoadAsync(
        () => showConcludedRaffles
            ? plugin.ApiClient.GetConcludedRafflesAsync(selectedRaffleGuildId)
            : plugin.ApiClient.GetOpenRafflesAsync(selectedRaffleGuildId),
        result => raffles = result,
        (loadState, err) => { raffleListLoadState = loadState; if (err != null) raffleListErrorMessage = err; },
        "Couldn't load raffles");
}
