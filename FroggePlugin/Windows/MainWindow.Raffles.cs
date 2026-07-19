using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Utility;
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

    private void DrawRaffles()
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
                DrawError(guildsErrorMessage, StartRaffles);
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
                        StartRaffleList(guild.GuildId, guild.GuildName);
                    ImGui.Spacing();
                }
                break;
        }
    }

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

        if (ColoredButton("Open", showConcludedRaffles ? MutedColor : AccentColor))
        {
            if (showConcludedRaffles)
            {
                showConcludedRaffles = false;
                StartRaffleFetch();
            }
        }
        ImGui.SameLine();
        if (ColoredButton("Concluded", showConcludedRaffles ? AccentColor : MutedColor))
        {
            if (!showConcludedRaffles)
            {
                showConcludedRaffles = true;
                StartRaffleFetch();
            }
        }
        ImGui.Spacing();
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
                            Util.OpenLink(link);
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

    private async Task FetchRafflesAsync()
    {
        try
        {
            var result = showConcludedRaffles
                ? await plugin.ApiClient.GetConcludedRafflesAsync(selectedRaffleGuildId)
                : await plugin.ApiClient.GetOpenRafflesAsync(selectedRaffleGuildId);
            if (result is null)
            {
                raffleListErrorMessage = "Couldn't load raffles.";
                raffleListLoadState = VipLoadState.Error;
                return;
            }

            raffles = result;
            raffleListLoadState = VipLoadState.Loaded;
        }
        catch (Exception ex)
        {
            raffleListErrorMessage = $"Couldn't load raffles: {ex.Message}";
            raffleListLoadState = VipLoadState.Error;
        }
    }
}
