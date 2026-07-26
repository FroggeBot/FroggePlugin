using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Objects.SubKinds;
using FroggePlugin.Api;

namespace FroggePlugin.Windows;

public partial class MainWindow
{
    private VipLoadState manageEventsLoadState = VipLoadState.Idle;
    private string? manageEventsErrorMessage;
    private List<PluginManageEventSummary>? manageEvents;
    private string createEventNameInput = string.Empty;

    private int selectedManageEventId;
    private VipLoadState manageEventDetailLoadState = VipLoadState.Idle;
    private string? manageEventDetailErrorMessage;
    private PluginManageEventDetail? manageEventDetail;

    // Edit buffers for the Description/Address/Image URL fields - each its own inline InputText +
    // Save button, mirroring Bot's "each own modal" as an inline field instead.
    private string manageEventDescriptionInput = string.Empty;
    private string manageEventAddressInput = string.Empty;
    private string manageEventImageUrlInput = string.Empty;

    // Backs the "Attach Position" catalog picker - own fields, not shared with staffingPositions*
    // (a different picker, different data), matching every prior picker's own-fields precedent.
    private VipLoadState eventCatalogPositionsLoadState = VipLoadState.Idle;
    private string? eventCatalogPositionsErrorMessage;
    private List<PluginPositionSummary>? eventCatalogPositions;
    private bool showingEventPositionPicker;

    // At most one shift's capacity is being edited at a time, rather than one input buffer per
    // shift (an unbounded, variable-length list) - "Edit Capacity" opens this single shared slot
    // for the clicked shift; a different shift's click just re-targets it.
    private int? editingCapacityShiftId;
    private string editingCapacityInput = string.Empty;

    // Which shift "Assign Member" is targeting - feeds the shared resolve-character flow
    // (resolveState/resolvedDiscordUserId/resolvedCharacterName, declared in MainWindow.ManageVip.cs),
    // this round's fourth consumer after VIP assign, Raffle ticket-crediting, and Staffing hire.
    private int selectedManageShiftId;

    // Guards a create/update/delete/attach/detach/shift/signup completion against firing after
    // the user has navigated away - same pattern as every other feature's own action-sequence
    // guard (vipActionSequence/staffingActionSequence/approvalActionSequence).
    private int manageEventsActionSequence;
    private bool manageEventsActionInProgress;
    private string? manageEventsActionErrorMessage;

    private void DrawManageEventList()
    {
        if (DrawBackButton())
        {
            page = Page.ManageVenue;
            manageEventsLoadState = VipLoadState.Idle;
            manageEvents = null;
            manageEventsErrorMessage = null;
            createEventNameInput = string.Empty;
            return;
        }

        ImGui.Separator();
        ImGui.Spacing();
        DrawTitle($"{selectedManageGuildName} - Events");
        ImGui.Spacing();

        DrawSectionHeader("Create Event");
        ImGui.SetNextItemWidth(-1);
        ImGui.InputText("##CreateEventName", ref createEventNameInput, 100);
        ImGui.BeginDisabled(manageEventsActionInProgress || string.IsNullOrWhiteSpace(createEventNameInput));
        if (ColoredButton("Create", AccentColor, FullWidthButton))
            StartCreateEvent();
        ImGui.EndDisabled();
        if (manageEventsActionErrorMessage is not null)
            DrawColored(manageEventsActionErrorMessage, DangerColor);
        ImGui.Spacing();

        switch (manageEventsLoadState)
        {
            case VipLoadState.Loading:
                DrawLoading();
                break;

            case VipLoadState.Error:
                DrawError(manageEventsErrorMessage, StartManageEventList);
                break;

            case VipLoadState.Loaded:
                if (manageEvents is null || manageEvents.Count == 0)
                {
                    DrawEmpty("No upcoming events yet.");
                    break;
                }

                foreach (var evt in manageEvents)
                {
                    BeginCard();
                    DrawTitle(evt.Name);
                    ImGui.TextDisabled(evt.StartAt.ToLocalTime().ToString("g"));
                    ImGui.Spacing();
                    if (ImGui.Button($"Manage##{evt.Id}"))
                        StartManageEventDetail(evt.Id);
                    EndCard();
                }
                break;
        }
    }

    private void StartManageEventList()
    {
        page = Page.ManageEventList;
        manageEventsLoadState = VipLoadState.Loading;
        manageEventsErrorMessage = null;
        manageEventsActionErrorMessage = null;
        createEventNameInput = string.Empty;
        _ = LoadAsync(
            () => plugin.ApiClient.GetManageEventsAsync(selectedManageGuildId),
            result => manageEvents = result,
            (state, msg) => { manageEventsLoadState = state; manageEventsErrorMessage = msg; },
            "Couldn't load events"
        );
    }

    private void StartCreateEvent()
    {
        manageEventsActionSequence++;
        var mySequence = manageEventsActionSequence;
        manageEventsActionInProgress = true;
        manageEventsActionErrorMessage = null;
        _ = PerformCreateEventAsync(mySequence, createEventNameInput);
    }

    private async Task PerformCreateEventAsync(int mySequence, string name)
    {
        try
        {
            var result = await plugin.ApiClient.CreateManageEventAsync(selectedManageGuildId, name);
            if (mySequence != manageEventsActionSequence)
                return; // navigated away - this completion is abandoned, don't touch shared state

            if (result is null)
            {
                manageEventsActionErrorMessage = "That action failed. Try again.";
                manageEventsActionInProgress = false;
                return;
            }

            manageEventsActionInProgress = false;
            createEventNameInput = string.Empty;
            manageEvents = null;
            StartManageEventList();
        }
        catch (Exception)
        {
            if (mySequence == manageEventsActionSequence)
            {
                manageEventsActionErrorMessage = "That action failed. Try again.";
                manageEventsActionInProgress = false;
            }
        }
    }

    private void DrawManageEventDetail()
    {
        if (DrawBackButton())
        {
            page = Page.ManageEventList;
            manageEventDetailLoadState = VipLoadState.Idle;
            manageEventDetail = null;
            manageEventDetailErrorMessage = null;
            showingEventPositionPicker = false;
            editingCapacityShiftId = null;
            manageEventsActionInProgress = false;
            manageEventsActionErrorMessage = null;
            manageEventsActionSequence++;
            return;
        }

        ImGui.Separator();
        ImGui.Spacing();

        switch (manageEventDetailLoadState)
        {
            case VipLoadState.Loading:
                DrawLoading();
                break;

            case VipLoadState.Error:
                DrawError(manageEventDetailErrorMessage, () => StartManageEventDetail(selectedManageEventId));
                break;

            case VipLoadState.Loaded:
                if (manageEventDetail is null)
                    break;

                var evt = manageEventDetail;
                BeginCard();
                DrawTitle(evt.Name);
                DrawInlineField("Starts", evt.StartAt.ToLocalTime().ToString("g"));
                if (evt.EndAt is { } endAt)
                    DrawInlineField("Ends", endAt.ToLocalTime().ToString("g"));
                DrawInlineField("Address", evt.Address);
                DrawInlineField("Image URL", evt.ImageUrl);
                if (!string.IsNullOrEmpty(evt.Description))
                    DrawLongField("Description", evt.Description);
                EndCard();

                ImGui.Spacing();
                if (manageEventsActionErrorMessage is not null)
                    DrawColored(manageEventsActionErrorMessage, DangerColor);

                DrawSectionHeader("Edit");

                ImGui.TextDisabled("Description");
                ImGui.SetNextItemWidth(-1);
                ImGui.InputText("##EventDescriptionInput", ref manageEventDescriptionInput, 2000);
                ImGui.BeginDisabled(manageEventsActionInProgress);
                if (ImGui.Button("Save Description"))
                    StartUpdateEvent(new { description = manageEventDescriptionInput });
                ImGui.EndDisabled();
                ImGui.Spacing();

                ImGui.TextDisabled("Address");
                ImGui.SetNextItemWidth(-1);
                ImGui.InputText("##EventAddressInput", ref manageEventAddressInput, 500);
                ImGui.BeginDisabled(manageEventsActionInProgress);
                if (ImGui.Button("Save Address"))
                    StartUpdateEvent(new { address = manageEventAddressInput });
                ImGui.EndDisabled();
                ImGui.Spacing();

                ImGui.TextDisabled("Image URL");
                ImGui.SetNextItemWidth(-1);
                ImGui.InputText("##EventImageUrlInput", ref manageEventImageUrlInput, 500);
                ImGui.BeginDisabled(manageEventsActionInProgress);
                if (ImGui.Button("Save Image URL"))
                    StartUpdateEvent(new { image_url = manageEventImageUrlInput });
                ImGui.EndDisabled();
                ImGui.Spacing();

                ImGui.BeginDisabled(manageEventsActionInProgress);
                if (ColoredButton("Delete Event", DangerColor))
                    StartDeleteEvent();
                ImGui.EndDisabled();

                DrawSectionHeader("Positions");
                foreach (var position in evt.Positions)
                {
                    BeginCard();
                    DrawTitle(position.PositionName);
                    ImGui.SameLine();
                    ImGui.BeginDisabled(manageEventsActionInProgress);
                    if (ImGui.Button($"Remove Position##{position.Id}"))
                        StartDetachPosition(position.Id);
                    ImGui.EndDisabled();

                    foreach (var shift in position.Shifts)
                    {
                        ImGui.Spacing();
                        ImGui.TextWrapped($"{shift.StartAt.ToLocalTime():g} - {shift.EndAt.ToLocalTime():t}");
                        ImGui.TextDisabled($"{shift.Signups.Count}/{shift.Capacity} filled");

                        foreach (var signupId in shift.Signups)
                        {
                            ImGui.TextWrapped(signupId.ToString());
                            ImGui.SameLine();
                            ImGui.BeginDisabled(manageEventsActionInProgress);
                            if (ImGui.Button($"Remove##{shift.Id}-{signupId}"))
                                StartRemoveSignup(shift.Id, signupId);
                            ImGui.EndDisabled();
                        }

                        if (editingCapacityShiftId == shift.Id)
                        {
                            ImGui.SetNextItemWidth(80);
                            ImGui.InputText($"##Capacity{shift.Id}", ref editingCapacityInput, 4, ImGuiInputTextFlags.CharsDecimal);
                            ImGui.SameLine();
                            ImGui.BeginDisabled(manageEventsActionInProgress);
                            if (ImGui.Button($"Save##Capacity{shift.Id}"))
                                StartSaveCapacity(shift.Id);
                            ImGui.EndDisabled();
                            ImGui.SameLine();
                            if (ImGui.Button($"Cancel##Capacity{shift.Id}"))
                                editingCapacityShiftId = null;
                        }
                        else
                        {
                            ImGui.BeginDisabled(manageEventsActionInProgress);
                            if (ImGui.Button($"Edit Capacity##{shift.Id}"))
                                StartEditCapacity(shift.Id, shift.Capacity);
                            ImGui.SameLine();
                            if (ImGui.Button($"Assign Member##{shift.Id}"))
                                StartManageEventAssignSignupTarget(shift.Id);
                            ImGui.SameLine();
                            if (ImGui.Button($"Delete Shift##{shift.Id}"))
                                StartDeleteShift(shift.Id);
                            ImGui.EndDisabled();
                        }
                    }

                    ImGui.Spacing();
                    ImGui.BeginDisabled(manageEventsActionInProgress);
                    if (ImGui.Button($"Add Shift##{position.Id}"))
                        StartCreateShift(position.Id);
                    ImGui.EndDisabled();

                    EndCard();
                }

                ImGui.Spacing();
                ImGui.BeginDisabled(manageEventsActionInProgress);
                if (ColoredButton("Attach Position", AccentColor))
                    StartAttachPositionPicker();
                ImGui.EndDisabled();

                if (showingEventPositionPicker)
                {
                    ImGui.Spacing();
                    DrawSectionHeader("Pick a Position");
                    DrawEventPositionPicker(StartAttachPositionPicker, (positionId, _) => StartAttachPosition(positionId));
                }
                break;
        }
    }

    private void StartManageEventDetail(int eventId)
    {
        page = Page.ManageEventDetail;
        selectedManageEventId = eventId;
        manageEventDetailLoadState = VipLoadState.Loading;
        manageEventDetailErrorMessage = null;
        showingEventPositionPicker = false;
        editingCapacityShiftId = null;
        manageEventsActionInProgress = false;
        manageEventsActionErrorMessage = null;
        manageEventsActionSequence++;
        _ = LoadAsync(
            () => plugin.ApiClient.GetManageEventDetailAsync(selectedManageGuildId, eventId),
            result =>
            {
                manageEventDetail = result;
                manageEventDescriptionInput = result.Description ?? string.Empty;
                manageEventAddressInput = result.Address ?? string.Empty;
                manageEventImageUrlInput = result.ImageUrl ?? string.Empty;
            },
            (state, msg) => { manageEventDetailLoadState = state; manageEventDetailErrorMessage = msg; },
            "Couldn't load event"
        );
    }

    private void StartUpdateEvent(object payload)
    {
        manageEventsActionSequence++;
        var mySequence = manageEventsActionSequence;
        manageEventsActionInProgress = true;
        manageEventsActionErrorMessage = null;
        _ = PerformUpdateEventAsync(mySequence, selectedManageEventId, payload);
    }

    private async Task PerformUpdateEventAsync(int mySequence, int eventId, object payload)
    {
        try
        {
            var result = await plugin.ApiClient.UpdateManageEventAsync(selectedManageGuildId, eventId, payload);
            if (mySequence != manageEventsActionSequence)
                return;

            if (result is null)
            {
                manageEventsActionErrorMessage = "That action failed. Try again.";
                manageEventsActionInProgress = false;
                return;
            }

            manageEventsActionInProgress = false;
            manageEventDetail = result;
        }
        catch (Exception)
        {
            if (mySequence == manageEventsActionSequence)
            {
                manageEventsActionErrorMessage = "That action failed. Try again.";
                manageEventsActionInProgress = false;
            }
        }
    }

    private void StartDeleteEvent()
    {
        manageEventsActionSequence++;
        var mySequence = manageEventsActionSequence;
        manageEventsActionInProgress = true;
        manageEventsActionErrorMessage = null;
        _ = PerformDeleteEventAsync(mySequence, selectedManageEventId);
    }

    private async Task PerformDeleteEventAsync(int mySequence, int eventId)
    {
        try
        {
            var success = await plugin.ApiClient.DeleteManageEventAsync(selectedManageGuildId, eventId);
            if (mySequence != manageEventsActionSequence)
                return;

            if (!success)
            {
                manageEventsActionErrorMessage = "That action failed. Try again.";
                manageEventsActionInProgress = false;
                return;
            }

            manageEventsActionInProgress = false;
            manageEvents = null;
            StartManageEventList();
        }
        catch (Exception)
        {
            if (mySequence == manageEventsActionSequence)
            {
                manageEventsActionErrorMessage = "That action failed. Try again.";
                manageEventsActionInProgress = false;
            }
        }
    }

    private void StartAttachPositionPicker()
    {
        showingEventPositionPicker = true;
        manageEventsActionErrorMessage = null;
        eventCatalogPositionsLoadState = VipLoadState.Loading;
        eventCatalogPositionsErrorMessage = null;
        _ = LoadAsync(
            () => plugin.ApiClient.GetEventCatalogPositionsAsync(selectedManageGuildId),
            result => eventCatalogPositions = result,
            (state, msg) => { eventCatalogPositionsLoadState = state; eventCatalogPositionsErrorMessage = msg; },
            "Couldn't load positions"
        );
    }

    private void StartAttachPosition(int positionId)
    {
        manageEventsActionSequence++;
        var mySequence = manageEventsActionSequence;
        manageEventsActionInProgress = true;
        manageEventsActionErrorMessage = null;
        _ = PerformAttachPositionAsync(mySequence, selectedManageEventId, positionId);
    }

    private async Task PerformAttachPositionAsync(int mySequence, int eventId, int positionId)
    {
        try
        {
            var result = await plugin.ApiClient.AttachEventPositionAsync(selectedManageGuildId, eventId, positionId);
            if (mySequence != manageEventsActionSequence)
                return;

            if (result is null)
            {
                manageEventsActionErrorMessage = "That action failed. Try again.";
                manageEventsActionInProgress = false;
                return;
            }

            // Attach returns only the new position (empty shifts) - re-fetch the whole detail
            // rather than hand-splicing, matching this codebase's re-fetch-after-mutation precedent.
            manageEventsActionInProgress = false;
            showingEventPositionPicker = false;
            StartManageEventDetail(eventId);
        }
        catch (Exception)
        {
            if (mySequence == manageEventsActionSequence)
            {
                manageEventsActionErrorMessage = "That action failed. Try again.";
                manageEventsActionInProgress = false;
            }
        }
    }

    private void StartDetachPosition(int eventPositionId)
    {
        manageEventsActionSequence++;
        var mySequence = manageEventsActionSequence;
        manageEventsActionInProgress = true;
        manageEventsActionErrorMessage = null;
        _ = PerformDetachPositionAsync(mySequence, selectedManageEventId, eventPositionId);
    }

    private async Task PerformDetachPositionAsync(int mySequence, int eventId, int eventPositionId)
    {
        try
        {
            var success = await plugin.ApiClient.DetachEventPositionAsync(selectedManageGuildId, eventPositionId);
            if (mySequence != manageEventsActionSequence)
                return;

            if (!success)
            {
                manageEventsActionErrorMessage = "That action failed. Try again.";
                manageEventsActionInProgress = false;
                return;
            }

            manageEventsActionInProgress = false;
            StartManageEventDetail(eventId);
        }
        catch (Exception)
        {
            if (mySequence == manageEventsActionSequence)
            {
                manageEventsActionErrorMessage = "That action failed. Try again.";
                manageEventsActionInProgress = false;
            }
        }
    }

    private void StartCreateShift(int eventPositionId)
    {
        manageEventsActionSequence++;
        var mySequence = manageEventsActionSequence;
        manageEventsActionInProgress = true;
        manageEventsActionErrorMessage = null;
        _ = PerformCreateShiftAsync(mySequence, selectedManageEventId, eventPositionId);
    }

    private async Task PerformCreateShiftAsync(int mySequence, int eventId, int eventPositionId)
    {
        try
        {
            var result = await plugin.ApiClient.CreateManageShiftAsync(selectedManageGuildId, eventPositionId);
            if (mySequence != manageEventsActionSequence)
                return;

            if (result is null)
            {
                manageEventsActionErrorMessage = "That action failed. Try again.";
                manageEventsActionInProgress = false;
                return;
            }

            manageEventsActionInProgress = false;
            StartManageEventDetail(eventId);
        }
        catch (Exception)
        {
            if (mySequence == manageEventsActionSequence)
            {
                manageEventsActionErrorMessage = "That action failed. Try again.";
                manageEventsActionInProgress = false;
            }
        }
    }

    private void StartEditCapacity(int shiftId, int currentCapacity)
    {
        editingCapacityShiftId = shiftId;
        editingCapacityInput = currentCapacity.ToString();
    }

    private void StartSaveCapacity(int shiftId)
    {
        if (!int.TryParse(editingCapacityInput, out var capacity) || capacity < 1)
        {
            manageEventsActionErrorMessage = "Capacity must be a positive number.";
            return;
        }

        manageEventsActionSequence++;
        var mySequence = manageEventsActionSequence;
        manageEventsActionInProgress = true;
        manageEventsActionErrorMessage = null;
        _ = PerformUpdateCapacityAsync(mySequence, selectedManageEventId, shiftId, capacity);
    }

    private async Task PerformUpdateCapacityAsync(int mySequence, int eventId, int shiftId, int capacity)
    {
        try
        {
            var result = await plugin.ApiClient.UpdateShiftCapacityAsync(selectedManageGuildId, shiftId, capacity);
            if (mySequence != manageEventsActionSequence)
                return;

            if (result is null)
            {
                manageEventsActionErrorMessage = "That action failed. Try again.";
                manageEventsActionInProgress = false;
                return;
            }

            manageEventsActionInProgress = false;
            editingCapacityShiftId = null;
            StartManageEventDetail(eventId);
        }
        catch (Exception)
        {
            if (mySequence == manageEventsActionSequence)
            {
                manageEventsActionErrorMessage = "That action failed. Try again.";
                manageEventsActionInProgress = false;
            }
        }
    }

    private void StartDeleteShift(int shiftId)
    {
        manageEventsActionSequence++;
        var mySequence = manageEventsActionSequence;
        manageEventsActionInProgress = true;
        manageEventsActionErrorMessage = null;
        _ = PerformDeleteShiftAsync(mySequence, selectedManageEventId, shiftId);
    }

    private async Task PerformDeleteShiftAsync(int mySequence, int eventId, int shiftId)
    {
        try
        {
            var success = await plugin.ApiClient.DeleteManageShiftAsync(selectedManageGuildId, shiftId);
            if (mySequence != manageEventsActionSequence)
                return;

            if (!success)
            {
                manageEventsActionErrorMessage = "That action failed. Try again.";
                manageEventsActionInProgress = false;
                return;
            }

            manageEventsActionInProgress = false;
            StartManageEventDetail(eventId);
        }
        catch (Exception)
        {
            if (mySequence == manageEventsActionSequence)
            {
                manageEventsActionErrorMessage = "That action failed. Try again.";
                manageEventsActionInProgress = false;
            }
        }
    }

    private void StartRemoveSignup(int shiftId, ulong discordUserId)
    {
        manageEventsActionSequence++;
        var mySequence = manageEventsActionSequence;
        manageEventsActionInProgress = true;
        manageEventsActionErrorMessage = null;
        _ = PerformRemoveSignupAsync(mySequence, selectedManageEventId, shiftId, discordUserId);
    }

    private async Task PerformRemoveSignupAsync(int mySequence, int eventId, int shiftId, ulong discordUserId)
    {
        try
        {
            var success = await plugin.ApiClient.RemoveEventSignupAsync(selectedManageGuildId, shiftId, discordUserId);
            if (mySequence != manageEventsActionSequence)
                return;

            if (!success)
            {
                manageEventsActionErrorMessage = "That action failed. Try again.";
                manageEventsActionInProgress = false;
                return;
            }

            manageEventsActionInProgress = false;
            StartManageEventDetail(eventId);
        }
        catch (Exception)
        {
            if (mySequence == manageEventsActionSequence)
            {
                manageEventsActionErrorMessage = "That action failed. Try again.";
                manageEventsActionInProgress = false;
            }
        }
    }

    private void DrawManageEventAssignSignupTarget()
    {
        if (DrawBackButton())
        {
            page = Page.ManageEventDetail;
            resolveState = VipLoadState.Idle;
            resolveErrorMessage = null;
            resolvedDiscordUserId = null;
            resolvedCharacterName = null;
            manageEventsActionInProgress = false;
            manageEventsActionErrorMessage = null;
            manageEventsActionSequence++;
            return;
        }

        ImGui.Separator();
        ImGui.Spacing();
        DrawTitle("Assign Member to Shift");
        ImGui.Spacing();
        ImGui.TextWrapped(
            "Target a player character in-game, then use their currently-targeted character to " +
            "look up the Discord account it's linked to via /verify."
        );
        ImGui.Spacing();

        var resolving = resolveState == VipLoadState.Loading;
        ImGui.BeginDisabled(resolving);
        if (ImGui.Button("Use Current Target"))
            StartResolveEventSignupTarget();
        ImGui.EndDisabled();

        ImGui.Spacing();
        switch (resolveState)
        {
            case VipLoadState.Loading:
                ImGui.TextDisabled("Looking up...");
                break;

            case VipLoadState.Error:
                DrawColored(resolveErrorMessage ?? "Couldn't resolve that character.", DangerColor);
                break;

            case VipLoadState.Loaded:
                if (resolvedDiscordUserId is not { } discordUserId)
                    break;

                DrawColored($"Found: {resolvedCharacterName}", SuccessColor);
                ImGui.Spacing();

                if (manageEventsActionErrorMessage is not null)
                    DrawColored(manageEventsActionErrorMessage, DangerColor);

                ImGui.BeginDisabled(manageEventsActionInProgress);
                if (ColoredButton("Assign", SuccessColor))
                    StartAssignSignup(discordUserId);
                ImGui.EndDisabled();
                break;
        }
    }

    private void StartManageEventAssignSignupTarget(int shiftId)
    {
        page = Page.ManageEventAssignSignupTarget;
        selectedManageShiftId = shiftId;
        resolveState = VipLoadState.Idle;
        resolveErrorMessage = null;
        resolvedDiscordUserId = null;
        resolvedCharacterName = null;
        manageEventsActionInProgress = false;
        manageEventsActionErrorMessage = null;
    }

    private void StartResolveEventSignupTarget()
    {
        resolveState = VipLoadState.Loading;
        resolveErrorMessage = null;
        resolvedDiscordUserId = null;
        _ = ResolveEventSignupTargetAsync();
    }

    // Reads the current target BEFORE the first await, deliberately - see ManageVip.cs's
    // ResolveTargetAsync for why (Dalamud game-state services are only main/framework-thread-safe,
    // and the sync portion up to the first `await` still runs on the render thread that called this).
    private async Task ResolveEventSignupTargetAsync()
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
                    "/verify in Discord first, or you can assign them from there instead.";
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

    private void StartAssignSignup(ulong discordUserId)
    {
        manageEventsActionSequence++;
        var mySequence = manageEventsActionSequence;
        manageEventsActionInProgress = true;
        manageEventsActionErrorMessage = null;
        _ = PerformAssignSignupAsync(mySequence, selectedManageEventId, selectedManageShiftId, discordUserId);
    }

    private async Task PerformAssignSignupAsync(int mySequence, int eventId, int shiftId, ulong discordUserId)
    {
        try
        {
            var result = await plugin.ApiClient.AssignEventSignupAsync(selectedManageGuildId, shiftId, discordUserId);
            if (mySequence != manageEventsActionSequence)
                return;

            if (result is null)
            {
                manageEventsActionErrorMessage = "That action failed. Try again.";
                manageEventsActionInProgress = false;
                return;
            }

            manageEventsActionInProgress = false;
            StartManageEventDetail(eventId);
        }
        catch (Exception)
        {
            if (mySequence == manageEventsActionSequence)
            {
                manageEventsActionErrorMessage = "That action failed. Try again.";
                manageEventsActionInProgress = false;
            }
        }
    }
}
