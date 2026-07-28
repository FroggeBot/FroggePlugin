using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using FroggePlugin.Api;

namespace FroggePlugin.Windows;

public partial class MainWindow
{
    // A merged, chronologically-sorted view across every linked venue - replaces the old
    // pick-a-venue-first flow entirely, since "what's on and when, across everywhere I attend"
    // is a more useful landing screen than a venue picker once there's more than one venue.
    private sealed record EventTimetableEntry(PluginEventSummary Event, string GuildName);

    private VipLoadState eventTimetableLoadState = VipLoadState.Idle;
    private string? eventTimetableErrorMessage;
    private List<EventTimetableEntry>? eventTimetable;

    // Which event's detail is being viewed - set when a timetable card's button is clicked.
    private ulong selectedEventGuildId;
    private string selectedEventGuildName = string.Empty;

    private int selectedEventId;
    private VipLoadState eventDetailLoadState = VipLoadState.Idle;
    private string? eventDetailErrorMessage;
    private PluginEventDetail? eventDetail;

    private void DrawEvents()
    {
        if (DrawBackButton())
        {
            page = Page.AttendingVenues;
            eventTimetableLoadState = VipLoadState.Idle;
            eventTimetable = null;
            eventTimetableErrorMessage = null;
            return;
        }

        ImGui.Separator();
        ImGui.Spacing();
        DrawTitle("Event Timetable");
        ImGui.Spacing();

        switch (eventTimetableLoadState)
        {
            case VipLoadState.Loading:
                DrawLoading();
                break;

            case VipLoadState.Error:
                DrawError(eventTimetableErrorMessage, StartEvents);
                break;

            case VipLoadState.Loaded:
                if (eventTimetable is null || eventTimetable.Count == 0)
                {
                    DrawEmpty("No upcoming events across your linked venues.");
                    break;
                }

                string? lastDay = null;
                foreach (var entry in eventTimetable)
                {
                    var day = entry.Event.StartAt.LocalDateTime.ToString("dddd, MMM d");
                    if (day != lastDay)
                    {
                        DrawSectionHeader(day);
                        lastDay = day;
                    }

                    var timeText = entry.Event.EndAt is { } endAt
                        ? $"{entry.Event.StartAt.LocalDateTime:t} - {endAt.LocalDateTime:t}"
                        : entry.Event.StartAt.LocalDateTime.ToString("t");
                    var rowText = $"{entry.Event.Name}  |  {entry.GuildName}  |  {timeText}";

                    BeginCard();

                    // No separate "View Shifts" button - the whole row is the click target.
                    // Reserve (InvisibleButton, sized to the wrapped text's own height) -> query
                    // -> draw, the same order every other clickable-but-not-a-plain-button
                    // widget in this file uses (DrawHomeTile/DrawRemoteImage). TextWrapped (via
                    // DrawColored/plain) wraps at the available width on its own - no manual
                    // wrap-width bookkeeping needed for the drawn text, only for measuring the
                    // InvisibleButton's reserved height ahead of it.
                    var wrapWidth = ImGui.GetContentRegionAvail().X;
                    var textSize = ImGui.CalcTextSize(rowText, false, wrapWidth);
                    var cursorStart = ImGui.GetCursorScreenPos();

                    var clicked = ImGui.InvisibleButton($"##event:{entry.Event.GuildId}:{entry.Event.Id}", new Vector2(wrapWidth, textSize.Y));
                    var hovered = ImGui.IsItemHovered();

                    ImGui.SetCursorScreenPos(cursorStart);
                    if (hovered)
                        DrawColored(rowText, AccentColor);
                    else
                        ImGui.TextWrapped(rowText);

                    if (clicked)
                        StartEventDetail(entry.Event.GuildId, entry.Event.Id, entry.GuildName);

                    EndCard(AccentColor);
                }
                break;
        }
    }

    private void StartEvents()
    {
        page = Page.Events;
        eventTimetableLoadState = VipLoadState.Loading;
        eventTimetableErrorMessage = null;
        _ = FetchEventTimetableAsync();
    }

    private async Task FetchEventTimetableAsync()
    {
        try
        {
            var guilds = await plugin.ApiClient.GetEventGuildsAsync();
            if (guilds is null)
            {
                eventTimetableLoadState = VipLoadState.Error;
                eventTimetableErrorMessage = "Couldn't load venues";
                return;
            }

            // Every plugin-facing event route is guild-scoped (matches every other module's
            // shape) - there's no single cross-guild "all my events" endpoint, so this merges
            // a handful of small per-guild fetches client-side instead.
            var perGuildResults = await Task.WhenAll(guilds.Select(async guild =>
            {
                var guildEvents = await plugin.ApiClient.GetUpcomingEventsAsync(guild.GuildId);
                return (guild.GuildName, Events: guildEvents ?? new List<PluginEventSummary>());
            }));

            eventTimetable = perGuildResults
                .SelectMany(result => result.Events.Select(e => new EventTimetableEntry(e, result.GuildName)))
                .OrderBy(entry => entry.Event.StartAt)
                .ToList();
            eventTimetableLoadState = VipLoadState.Loaded;
        }
        catch (Exception ex)
        {
            eventTimetableLoadState = VipLoadState.Error;
            eventTimetableErrorMessage = $"Couldn't load events: {ex.Message}";
        }
    }

    private void DrawEventDetail()
    {
        if (DrawBackButton())
        {
            page = Page.Events;
            eventDetailLoadState = VipLoadState.Idle;
            eventDetail = null;
            eventDetailErrorMessage = null;
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
                DrawError(eventDetailErrorMessage, () => StartEventDetailCore(selectedEventGuildId, selectedEventId));
                break;

            case VipLoadState.Loaded:
                if (eventDetail is null)
                    break;

                // Attendee-facing info card - no shift browsing/signup here (that's a Discord-only
                // flow for now), just what the event actually is: name, venue, a thumbnail if the
                // admin set one, the description, and where it's happening.
                DrawTitle(eventDetail.Name);
                ImGui.TextDisabled(selectedEventGuildName);
                ImGui.Spacing();

                if (eventDetail.ImageUrl is not null)
                {
                    DrawRemoteImage(eventDetail.ImageUrl, GetPlaceholderBannerTexture(),
                        new Vector2(Math.Min(ImGui.GetContentRegionAvail().X, 320), 120));
                    ImGui.Spacing();
                }

                if (eventDetail.Description is not null)
                {
                    ImGui.TextWrapped(eventDetail.Description);
                    ImGui.Spacing();
                }

                if (eventDetail.Address is not null)
                    ImGui.TextWrapped($"📍 {eventDetail.Address}");
                break;
        }
    }

    // Entry point from the timetable, which already has the venue name on hand for the entry
    // being clicked - takes it directly rather than re-deriving it from state.
    private void StartEventDetail(ulong guildId, int eventId, string guildName)
    {
        selectedEventGuildName = guildName;
        StartEventDetailCore(guildId, eventId);
    }

    // Retry path (DrawError's callback below) reuses whatever selectedEventGuildName is already
    // set to - it's not changing venues, just re-fetching the same event.
    private void StartEventDetailCore(ulong guildId, int eventId)
    {
        page = Page.EventDetail;
        selectedEventGuildId = guildId;
        selectedEventId = eventId;
        eventDetailLoadState = VipLoadState.Loading;
        eventDetailErrorMessage = null;
        _ = FetchEventDetailAsync(guildId, eventId);
    }

    private Task FetchEventDetailAsync(ulong guildId, int eventId) => LoadAsync(
        () => plugin.ApiClient.GetEventDetailAsync(guildId, eventId),
        result => eventDetail = result,
        (loadState, err) => { eventDetailLoadState = loadState; if (err != null) eventDetailErrorMessage = err; },
        "Couldn't load event");
}
