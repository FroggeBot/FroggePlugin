using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Utility;
using FroggePlugin.Api;

namespace FroggePlugin.Windows;

public partial class MainWindow
{
    // Which venue's giveaways are being browsed - set when a venue button is clicked in DrawGiveaways.
    private ulong selectedGiveawayGuildId;
    private string selectedGiveawayGuildName = string.Empty;

    private bool showConcludedGiveaways;
    private VipLoadState giveawayListLoadState = VipLoadState.Idle;
    private string? giveawayListErrorMessage;
    private List<PluginGiveawaySummary>? giveaways;

    private void DrawGiveaways()
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
                DrawError(guildsErrorMessage, StartGiveaways);
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
                        StartGiveawayList(guild.GuildId, guild.GuildName);
                    ImGui.Spacing();
                }
                break;
        }
    }

    private void StartGiveaways()
    {
        page = Page.Giveaways;
        guildsLoadState = VipLoadState.Loading;
        guildsErrorMessage = null;
        _ = FetchGuildsAsync();
    }

    private async Task FetchGuildsAsync()
    {
        try
        {
            var result = await plugin.ApiClient.GetGuildsAsync();
            if (result is null)
            {
                guildsErrorMessage = "Couldn't load venues.";
                guildsLoadState = VipLoadState.Error;
                return;
            }

            guilds = result;
            guildsLoadState = VipLoadState.Loaded;
        }
        catch (Exception ex)
        {
            guildsErrorMessage = $"Couldn't load venues: {ex.Message}";
            guildsLoadState = VipLoadState.Error;
        }
    }

    private void DrawGiveawayList()
    {
        if (DrawBackButton())
        {
            page = Page.Giveaways;
            giveawayListLoadState = VipLoadState.Idle;
            giveaways = null;
            giveawayListErrorMessage = null;
            return;
        }

        ImGui.Separator();
        ImGui.Spacing();
        DrawTitle(selectedGiveawayGuildName);
        ImGui.Spacing();

        if (ColoredButton("Open", showConcludedGiveaways ? MutedColor : AccentColor))
        {
            if (showConcludedGiveaways)
            {
                showConcludedGiveaways = false;
                StartGiveawayFetch();
            }
        }
        ImGui.SameLine();
        if (ColoredButton("Concluded", showConcludedGiveaways ? AccentColor : MutedColor))
        {
            if (!showConcludedGiveaways)
            {
                showConcludedGiveaways = true;
                StartGiveawayFetch();
            }
        }
        ImGui.Spacing();
        ImGui.Spacing();

        switch (giveawayListLoadState)
        {
            case VipLoadState.Loading:
                DrawLoading();
                break;

            case VipLoadState.Error:
                DrawError(giveawayListErrorMessage, StartGiveawayFetch);
                break;

            case VipLoadState.Loaded:
                if (giveaways is null || giveaways.Count == 0)
                {
                    DrawEmpty(showConcludedGiveaways ? "No concluded giveaways yet." : "No open giveaways.");
                    break;
                }

                foreach (var giveaway in giveaways)
                {
                    BeginCard();
                    DrawTitle(giveaway.Name ?? "(unnamed giveaway)");
                    if (!string.IsNullOrEmpty(giveaway.Prize))
                        ImGui.TextWrapped(giveaway.Prize);

                    if (giveaway.IsRolled)
                    {
                        ImGui.TextDisabled(giveaway.RolledAt is { } rolledAt
                            ? $"Concluded {rolledAt.LocalDateTime:g}"
                            : "Concluded");
                    }
                    else
                    {
                        ImGui.TextDisabled(giveaway.EndAt is { } endAt
                            ? $"Ends {endAt.LocalDateTime:g}"
                            : "No end date set");
                    }

                    ImGui.TextDisabled($"{giveaway.EntrantCount} entrant(s)");

                    if (giveaway.IsEntered)
                    {
                        DrawBadge("Entered", AccentColor);
                        ImGui.SameLine();
                    }
                    if (giveaway is { IsRolled: true, IsWinner: true })
                        DrawBadge("You Won!", SuccessColor);

                    if (giveaway.DiscordLink is { } link)
                    {
                        ImGui.Spacing();
                        if (ImGui.Button($"View in Discord##{giveaway.Id}"))
                            Util.OpenLink(link);
                    }

                    EndCard(AccentColor);
                }
                break;
        }
    }

    private void StartGiveawayList(ulong guildId, string guildName)
    {
        page = Page.GiveawayList;
        selectedGiveawayGuildId = guildId;
        selectedGiveawayGuildName = guildName;
        showConcludedGiveaways = false;
        StartGiveawayFetch();
    }

    private void StartGiveawayFetch()
    {
        giveawayListLoadState = VipLoadState.Loading;
        giveawayListErrorMessage = null;
        _ = FetchGiveawaysAsync();
    }

    private async Task FetchGiveawaysAsync()
    {
        try
        {
            var result = showConcludedGiveaways
                ? await plugin.ApiClient.GetConcludedGiveawaysAsync(selectedGiveawayGuildId)
                : await plugin.ApiClient.GetOpenGiveawaysAsync(selectedGiveawayGuildId);
            if (result is null)
            {
                giveawayListErrorMessage = "Couldn't load giveaways.";
                giveawayListLoadState = VipLoadState.Error;
                return;
            }

            giveaways = result;
            giveawayListLoadState = VipLoadState.Loaded;
        }
        catch (Exception ex)
        {
            giveawayListErrorMessage = $"Couldn't load giveaways: {ex.Message}";
            giveawayListLoadState = VipLoadState.Error;
        }
    }
}
