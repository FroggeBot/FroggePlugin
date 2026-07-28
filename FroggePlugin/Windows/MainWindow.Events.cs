using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using FroggePlugin.Api;

namespace FroggePlugin.Windows;

public partial class MainWindow
{
    private VipLoadState eventGuildsLoadState = VipLoadState.Idle;
    private string? eventGuildsErrorMessage;
    private List<PluginEventGuild>? eventGuilds;

    // Which venue's events are being browsed - set when a venue button is clicked in DrawEvents.
    private ulong selectedEventGuildId;
    private string selectedEventGuildName = string.Empty;

    private VipLoadState eventListLoadState = VipLoadState.Idle;
    private string? eventListErrorMessage;
    private List<PluginEventSummary>? events;

    private int selectedEventId;
    private VipLoadState eventDetailLoadState = VipLoadState.Idle;
    private string? eventDetailErrorMessage;
    private PluginEventDetail? eventDetail;

    // Guards a shift signup/leave action against a stale completion writing over newer state -
    // bumped on every new action AND every navigation away from/into EventDetail, so a completion
    // whose sequence no longer matches is known-abandoned and must not touch shared fields.
    private int actionSequence;
    private bool shiftActionInProgress;
    private string? shiftActionErrorMessage;

    private void DrawEvents()
    {
        if (DrawBackButton())
        {
            page = Page.Home;
            eventGuildsLoadState = VipLoadState.Idle;
            eventGuilds = null;
            eventGuildsErrorMessage = null;
            return;
        }

        ImGui.Separator();
        ImGui.Spacing();

        switch (eventGuildsLoadState)
        {
            case VipLoadState.Loading:
                DrawLoading();
                break;

            case VipLoadState.Error:
                DrawError(eventGuildsErrorMessage, StartEvents);
                break;

            case VipLoadState.Loaded:
                if (eventGuilds is null || eventGuilds.Count == 0)
                {
                    DrawEmpty("No linked venues yet. Run /plugin-link again if you've joined a new one.");
                    break;
                }

                foreach (var guild in eventGuilds)
                {
                    if (ColoredButton($"{guild.GuildName}##{guild.GuildId}", AccentColor, FullWidthButton))
                        StartEventList(guild.GuildId, guild.GuildName);
                    ImGui.Spacing();
                }
                break;
        }
    }

    private void StartEvents()
    {
        page = Page.Events;
        eventGuildsLoadState = VipLoadState.Loading;
        eventGuildsErrorMessage = null;
        _ = FetchEventGuildsAsync();
    }

    private Task FetchEventGuildsAsync() => LoadAsync(
        plugin.ApiClient.GetEventGuildsAsync,
        result => eventGuilds = result,
        (loadState, err) => { eventGuildsLoadState = loadState; if (err != null) eventGuildsErrorMessage = err; },
        "Couldn't load venues");

    private void DrawEventList()
    {
        if (DrawBackButton())
        {
            page = Page.Events;
            eventListLoadState = VipLoadState.Idle;
            events = null;
            eventListErrorMessage = null;
            return;
        }

        ImGui.Separator();
        ImGui.Spacing();
        DrawTitle(selectedEventGuildName);
        ImGui.Spacing();

        switch (eventListLoadState)
        {
            case VipLoadState.Loading:
                DrawLoading();
                break;

            case VipLoadState.Error:
                DrawError(eventListErrorMessage, () => StartEventList(selectedEventGuildId, selectedEventGuildName));
                break;

            case VipLoadState.Loaded:
                if (events is null || events.Count == 0)
                {
                    DrawEmpty("No upcoming events.");
                    break;
                }

                foreach (var evt in events)
                {
                    BeginCard();
                    DrawTitle(evt.Name);
                    ImGui.TextDisabled(evt.StartAt.LocalDateTime.ToString("g"));
                    ImGui.Spacing();
                    if (ImGui.Button($"View Shifts##{evt.Id}"))
                        StartEventDetail(selectedEventGuildId, evt.Id);
                    EndCard(AccentColor);
                }
                break;
        }
    }

    private void StartEventList(ulong guildId, string guildName)
    {
        page = Page.EventList;
        selectedEventGuildId = guildId;
        selectedEventGuildName = guildName;
        eventListLoadState = VipLoadState.Loading;
        eventListErrorMessage = null;
        _ = FetchEventListAsync(guildId);
    }

    private Task FetchEventListAsync(ulong guildId) => LoadAsync(
        () => plugin.ApiClient.GetUpcomingEventsAsync(guildId),
        result => events = result,
        (loadState, err) => { eventListLoadState = loadState; if (err != null) eventListErrorMessage = err; },
        "Couldn't load events");

    private void DrawEventDetail()
    {
        if (DrawBackButton())
        {
            page = Page.EventList;
            eventDetailLoadState = VipLoadState.Idle;
            eventDetail = null;
            eventDetailErrorMessage = null;
            shiftActionInProgress = false;
            shiftActionErrorMessage = null;
            actionSequence++; // invalidate any in-flight shift action tied to this view
            return;
        }

        ImGui.Separator();
        ImGui.Spacing();

        switch (eventDetailLoadState)
        {
            case VipLoadState.Loading:
                DrawLoading();
                break;

            case VipLoadState.Error:
                DrawError(eventDetailErrorMessage, () => StartEventDetail(selectedEventGuildId, selectedEventId));
                break;

            case VipLoadState.Loaded:
                if (eventDetail is null)
                    break;

                DrawTitle(eventDetail.Name);
                if (eventDetail.Description is not null)
                    ImGui.TextWrapped(eventDetail.Description);
                if (shiftActionErrorMessage is not null)
                    DrawColored(shiftActionErrorMessage, DangerColor);
                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();

                foreach (var position in eventDetail.Positions)
                {
                    DrawSectionHeader(position.PositionName);
                    foreach (var shift in position.Shifts)
                    {
                        var fullyBooked = !shift.IsSignedUp && shift.SignupCount >= shift.Capacity;
                        var (label, color) = shift.IsLocked
                            ? ("Locked", MutedColor)
                            : shift.IsSignedUp
                                ? ("Leave", DangerColor)
                                : ("Join", SuccessColor);
                        var disabled = shiftActionInProgress || shift.IsLocked || fullyBooked;

                        BeginCard();
                        ImGui.TextWrapped($"{shift.StartAt.LocalDateTime:t}-{shift.EndAt.LocalDateTime:t}");
                        DrawColored(
                            $"{shift.SignupCount}/{shift.Capacity} filled",
                            fullyBooked ? WarningColor : MutedColor
                        );
                        ImGui.Spacing();

                        ImGui.BeginDisabled(disabled);
                        if (ColoredButton($"{label}##{shift.Id}", color))
                            StartShiftAction(selectedEventGuildId, selectedEventId, shift.Id, !shift.IsSignedUp);
                        ImGui.EndDisabled();
                        EndCard(color);
                    }
                    ImGui.Spacing();
                }
                break;
        }
    }

    private void StartEventDetail(ulong guildId, int eventId)
    {
        page = Page.EventDetail;
        selectedEventGuildId = guildId;
        selectedEventId = eventId;
        eventDetailLoadState = VipLoadState.Loading;
        eventDetailErrorMessage = null;
        shiftActionInProgress = false;
        shiftActionErrorMessage = null;
        actionSequence++; // invalidate any in-flight shift action from a previous visit
        _ = FetchEventDetailAsync(guildId, eventId);
    }

    private Task FetchEventDetailAsync(ulong guildId, int eventId) => LoadAsync(
        () => plugin.ApiClient.GetEventDetailAsync(guildId, eventId),
        result => eventDetail = result,
        (loadState, err) => { eventDetailLoadState = loadState; if (err != null) eventDetailErrorMessage = err; },
        "Couldn't load event");

    private void StartShiftAction(ulong guildId, int eventId, int shiftId, bool isSignup)
    {
        actionSequence++;
        var mySequence = actionSequence;
        shiftActionInProgress = true;
        shiftActionErrorMessage = null;
        _ = PerformShiftActionAsync(mySequence, guildId, eventId, shiftId, isSignup);
    }

    private async Task PerformShiftActionAsync(int mySequence, ulong guildId, int eventId, int shiftId, bool isSignup)
    {
        try
        {
            var success = isSignup
                ? await plugin.ApiClient.SignupForShiftAsync(guildId, shiftId)
                : await plugin.ApiClient.LeaveShiftAsync(guildId, shiftId);

            if (mySequence != actionSequence)
                return; // navigated away - this completion is abandoned, don't touch shared state

            if (!success)
            {
                shiftActionErrorMessage = "That action failed. Try again.";
                shiftActionInProgress = false;
                return;
            }

            var detail = await plugin.ApiClient.GetEventDetailAsync(guildId, eventId);
            if (mySequence != actionSequence)
                return;

            if (detail is not null)
                eventDetail = detail;
            shiftActionInProgress = false;
        }
        catch (Exception)
        {
            if (mySequence == actionSequence)
            {
                shiftActionErrorMessage = "That action failed. Try again.";
                shiftActionInProgress = false;
            }
        }
    }
}
