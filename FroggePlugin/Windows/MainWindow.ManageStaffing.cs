using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Objects.SubKinds;
using FroggePlugin.Api;

namespace FroggePlugin.Windows;

public partial class MainWindow
{
    private VipLoadState staffMembersLoadState = VipLoadState.Idle;
    private string? staffMembersErrorMessage;
    private List<PluginManageStaffMemberSummary>? staffMembers;

    private int selectedStaffMemberId;
    private VipLoadState staffMemberDetailLoadState = VipLoadState.Idle;
    private string? staffMemberDetailErrorMessage;
    private PluginManageStaffMemberDetail? staffMemberDetail;

    // Backs the "Add Qualification" position picker on the member-detail screen - own fields,
    // not shared with VIP's vipTiers* (a different picker, different data), mirroring Raffles'
    // own-fields-not-shared-with-Giveaways precedent.
    private VipLoadState staffingPositionsLoadState = VipLoadState.Idle;
    private string? staffingPositionsErrorMessage;
    private List<PluginPositionSummary>? staffingPositions;
    private bool showingPositionPicker;

    // Guards a hire/terminate/rehire/qualification completion against firing after the user has
    // navigated away - same pattern as ManageVip.cs's vipActionSequence, kept as its own field
    // rather than reused since the two features' actions are otherwise unrelated.
    private int staffingActionSequence;
    private bool staffingActionInProgress;
    private string? staffingActionErrorMessage;

    private void DrawManageStaffingRoster()
    {
        if (DrawBackButton())
        {
            page = Page.ManageVenue;
            staffMembersLoadState = VipLoadState.Idle;
            staffMembers = null;
            staffMembersErrorMessage = null;
            return;
        }

        ImGui.Separator();
        ImGui.Spacing();
        DrawTitle($"{selectedManageGuildName} - Staffing");
        ImGui.Spacing();

        if (ColoredButton("Hire New (via Target)", AccentColor, FullWidthButton))
            StartManageStaffingHireTarget();
        ImGui.Spacing();

        switch (staffMembersLoadState)
        {
            case VipLoadState.Loading:
                DrawLoading();
                break;

            case VipLoadState.Error:
                DrawError(staffMembersErrorMessage, StartManageStaffingRoster);
                break;

            case VipLoadState.Loaded:
                if (staffMembers is null || staffMembers.Count == 0)
                {
                    DrawEmpty("No staff members yet.");
                    break;
                }

                foreach (var member in staffMembers)
                {
                    BeginCard();
                    DrawTitle(member.DisplayName);
                    ImGui.TextDisabled(member.IsTerminated ? "Terminated" : "Active");
                    ImGui.Spacing();
                    if (ImGui.Button($"View##{member.Id}"))
                        StartManageStaffMemberDetail(member.Id);
                    EndCard(member.IsTerminated ? MutedColor : AccentColor);
                }
                break;
        }
    }

    private void StartManageStaffingRoster()
    {
        page = Page.ManageStaffingRoster;
        staffMembersLoadState = VipLoadState.Loading;
        staffMembersErrorMessage = null;
        _ = LoadAsync(
            () => plugin.ApiClient.GetManageStaffingRosterAsync(selectedManageGuildId),
            result => staffMembers = result,
            (state, msg) => { staffMembersLoadState = state; staffMembersErrorMessage = msg; },
            "Couldn't load staff roster"
        );
    }

    private void DrawManageStaffingMemberDetail()
    {
        if (DrawBackButton())
        {
            page = Page.ManageStaffingRoster;
            staffMemberDetailLoadState = VipLoadState.Idle;
            staffMemberDetail = null;
            staffMemberDetailErrorMessage = null;
            showingPositionPicker = false;
            staffingActionInProgress = false;
            staffingActionErrorMessage = null;
            staffingActionSequence++;
            return;
        }

        ImGui.Separator();
        ImGui.Spacing();

        switch (staffMemberDetailLoadState)
        {
            case VipLoadState.Loading:
                DrawLoading();
                break;

            case VipLoadState.Error:
                DrawError(staffMemberDetailErrorMessage, () => StartManageStaffMemberDetail(selectedStaffMemberId));
                break;

            case VipLoadState.Loaded:
                if (staffMemberDetail is null)
                    break;

                var member = staffMemberDetail;
                BeginCard();
                DrawTitle(member.DisplayName);
                ImGui.Spacing();
                DrawInlineField("Status", member.IsTerminated ? "Terminated" : "Active");
                DrawInlineField("Birthday", member.Birthday);
                DrawInlineField("Notes", member.Notes);
                if (member.Positions.Count > 0)
                {
                    ImGui.Spacing();
                    DrawSectionHeader("Qualifications");
                    foreach (var position in member.Positions)
                    {
                        ImGui.TextWrapped(position.Name);
                        ImGui.SameLine();
                        ImGui.BeginDisabled(staffingActionInProgress);
                        if (ImGui.Button($"Remove##{position.Id}"))
                            StartRemoveQualification(position.Id);
                        ImGui.EndDisabled();
                    }
                }
                EndCard(member.IsTerminated ? MutedColor : AccentColor);

                ImGui.Spacing();
                if (staffingActionErrorMessage is not null)
                    DrawColored(staffingActionErrorMessage, DangerColor);

                ImGui.BeginDisabled(staffingActionInProgress);
                if (member.IsTerminated)
                {
                    if (ColoredButton("Rehire", SuccessColor))
                        StartRehireStaffMember();
                }
                else
                {
                    if (ColoredButton("Terminate", DangerColor))
                        StartTerminateStaffMember();
                }
                ImGui.SameLine();
                if (ColoredButton("Add Qualification", AccentColor))
                    StartAddQualificationPicker();
                ImGui.EndDisabled();

                if (showingPositionPicker)
                {
                    ImGui.Spacing();
                    DrawSectionHeader("Pick a Position");
                    DrawPositionPicker(StartAddQualificationPicker, (positionId, _) => StartAddQualification(positionId));
                }
                break;
        }
    }

    private void StartManageStaffMemberDetail(int memberId)
    {
        page = Page.ManageStaffingMemberDetail;
        selectedStaffMemberId = memberId;
        staffMemberDetailLoadState = VipLoadState.Loading;
        staffMemberDetailErrorMessage = null;
        showingPositionPicker = false;
        staffingActionInProgress = false;
        staffingActionErrorMessage = null;
        staffingActionSequence++;
        _ = LoadAsync(
            () => plugin.ApiClient.GetManageStaffMemberDetailAsync(selectedManageGuildId, memberId),
            result => staffMemberDetail = result,
            (state, msg) => { staffMemberDetailLoadState = state; staffMemberDetailErrorMessage = msg; },
            "Couldn't load staff member"
        );
    }

    private void StartAddQualificationPicker()
    {
        showingPositionPicker = true;
        StartLoadStaffingPositions();
    }

    private void StartLoadStaffingPositions()
    {
        staffingPositionsLoadState = VipLoadState.Loading;
        staffingPositionsErrorMessage = null;
        _ = LoadAsync(
            () => plugin.ApiClient.GetManageStaffingPositionsAsync(selectedManageGuildId),
            result => staffingPositions = result,
            (state, msg) => { staffingPositionsLoadState = state; staffingPositionsErrorMessage = msg; },
            "Couldn't load positions"
        );
    }

    private void StartTerminateStaffMember()
    {
        staffingActionSequence++;
        var mySequence = staffingActionSequence;
        staffingActionInProgress = true;
        staffingActionErrorMessage = null;
        _ = PerformTerminateAsync(mySequence, selectedStaffMemberId);
    }

    private async Task PerformTerminateAsync(int mySequence, int memberId)
    {
        try
        {
            var result = await plugin.ApiClient.TerminateStaffMemberAsync(selectedManageGuildId, memberId);
            if (mySequence != staffingActionSequence)
                return; // navigated away - this completion is abandoned, don't touch shared state

            if (result is null)
            {
                staffingActionErrorMessage = "That action failed. Try again.";
                staffingActionInProgress = false;
                return;
            }

            staffingActionInProgress = false;
            staffMemberDetail = result;
        }
        catch (Exception)
        {
            if (mySequence == staffingActionSequence)
            {
                staffingActionErrorMessage = "That action failed. Try again.";
                staffingActionInProgress = false;
            }
        }
    }

    private void StartRehireStaffMember()
    {
        staffingActionSequence++;
        var mySequence = staffingActionSequence;
        staffingActionInProgress = true;
        staffingActionErrorMessage = null;
        _ = PerformRehireAsync(mySequence, selectedStaffMemberId);
    }

    private async Task PerformRehireAsync(int mySequence, int memberId)
    {
        try
        {
            var result = await plugin.ApiClient.RehireStaffMemberAsync(selectedManageGuildId, memberId);
            if (mySequence != staffingActionSequence)
                return;

            if (result is null)
            {
                staffingActionErrorMessage = "That action failed. Try again.";
                staffingActionInProgress = false;
                return;
            }

            staffingActionInProgress = false;
            staffMemberDetail = result;
        }
        catch (Exception)
        {
            if (mySequence == staffingActionSequence)
            {
                staffingActionErrorMessage = "That action failed. Try again.";
                staffingActionInProgress = false;
            }
        }
    }

    private void StartAddQualification(int positionId)
    {
        staffingActionSequence++;
        var mySequence = staffingActionSequence;
        staffingActionInProgress = true;
        staffingActionErrorMessage = null;
        _ = PerformAddQualificationAsync(mySequence, selectedStaffMemberId, positionId);
    }

    private async Task PerformAddQualificationAsync(int mySequence, int memberId, int positionId)
    {
        try
        {
            var result = await plugin.ApiClient.AddStaffQualificationAsync(selectedManageGuildId, memberId, positionId);
            if (mySequence != staffingActionSequence)
                return;

            if (result is null)
            {
                staffingActionErrorMessage = "That action failed. Try again.";
                staffingActionInProgress = false;
                return;
            }

            staffingActionInProgress = false;
            showingPositionPicker = false;
            staffMemberDetail = result;
        }
        catch (Exception)
        {
            if (mySequence == staffingActionSequence)
            {
                staffingActionErrorMessage = "That action failed. Try again.";
                staffingActionInProgress = false;
            }
        }
    }

    private void StartRemoveQualification(int positionId)
    {
        staffingActionSequence++;
        var mySequence = staffingActionSequence;
        staffingActionInProgress = true;
        staffingActionErrorMessage = null;
        _ = PerformRemoveQualificationAsync(mySequence, selectedStaffMemberId, positionId);
    }

    private async Task PerformRemoveQualificationAsync(int mySequence, int memberId, int positionId)
    {
        try
        {
            var success = await plugin.ApiClient.RemoveStaffQualificationAsync(selectedManageGuildId, memberId, positionId);
            if (mySequence != staffingActionSequence)
                return;

            if (!success)
            {
                staffingActionErrorMessage = "That action failed. Try again.";
                staffingActionInProgress = false;
                return;
            }

            staffingActionInProgress = false;
            // Removal returns 204 No Content (no updated detail payload) - re-fetch to pick up the
            // now-shorter qualifications list, matching ManageVip.cs's own re-fetch-after-mutation pattern.
            StartManageStaffMemberDetail(memberId);
        }
        catch (Exception)
        {
            if (mySequence == staffingActionSequence)
            {
                staffingActionErrorMessage = "That action failed. Try again.";
                staffingActionInProgress = false;
            }
        }
    }

    private void DrawManageStaffingHireTarget()
    {
        if (DrawBackButton())
        {
            page = Page.ManageStaffingRoster;
            resolveState = VipLoadState.Idle;
            resolveErrorMessage = null;
            resolvedDiscordUserId = null;
            resolvedCharacterName = null;
            staffingActionInProgress = false;
            staffingActionErrorMessage = null;
            staffingActionSequence++;
            return;
        }

        ImGui.Separator();
        ImGui.Spacing();
        DrawTitle("Hire Staff Member");
        ImGui.Spacing();
        ImGui.TextWrapped(
            "Target a player character in-game, then use their currently-targeted character to " +
            "look up the Discord account it's linked to via /verify."
        );
        ImGui.Spacing();

        var resolving = resolveState == VipLoadState.Loading;
        ImGui.BeginDisabled(resolving);
        if (ColoredButton("Use Current Target", AccentColor))
            StartResolveStaffTarget();
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

                if (staffingActionErrorMessage is not null)
                    DrawColored(staffingActionErrorMessage, DangerColor);

                ImGui.BeginDisabled(staffingActionInProgress);
                if (ColoredButton("Hire", SuccessColor))
                    StartHireResolvedMember(discordUserId);
                ImGui.EndDisabled();
                break;
        }
    }

    private void StartManageStaffingHireTarget()
    {
        page = Page.ManageStaffingHireTarget;
        resolveState = VipLoadState.Idle;
        resolveErrorMessage = null;
        resolvedDiscordUserId = null;
        resolvedCharacterName = null;
        staffingActionInProgress = false;
        staffingActionErrorMessage = null;
    }

    private void StartResolveStaffTarget()
    {
        resolveState = VipLoadState.Loading;
        resolveErrorMessage = null;
        resolvedDiscordUserId = null;
        _ = ResolveStaffTargetAsync();
    }

    // Reads the current target BEFORE the first await, deliberately - see ManageVip.cs's
    // ResolveTargetAsync for why (Dalamud game-state services are only main/framework-thread-safe,
    // and the sync portion up to the first `await` still runs on the render thread that called this).
    private async Task ResolveStaffTargetAsync()
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
                    "/verify in Discord first, or you can hire them from there instead.";
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

    private void StartHireResolvedMember(ulong discordUserId)
    {
        staffingActionSequence++;
        var mySequence = staffingActionSequence;
        staffingActionInProgress = true;
        staffingActionErrorMessage = null;
        _ = PerformHireResolvedMemberAsync(mySequence, discordUserId);
    }

    private async Task PerformHireResolvedMemberAsync(int mySequence, ulong discordUserId)
    {
        try
        {
            var result = await plugin.ApiClient.HireStaffMemberAsync(selectedManageGuildId, discordUserId);
            if (mySequence != staffingActionSequence)
                return;

            if (result is null)
            {
                staffingActionErrorMessage = "That action failed. Try again.";
                staffingActionInProgress = false;
                return;
            }

            // The roster is now stale - land back on it, which is the natural next screen and
            // re-fetches fresh, matching ManageVip.cs's PerformAssignAsync's own precedent.
            staffingActionInProgress = false;
            staffMembers = null;
            StartManageStaffingRoster();
        }
        catch (Exception)
        {
            if (mySequence == staffingActionSequence)
            {
                staffingActionErrorMessage = "That action failed. Try again.";
                staffingActionInProgress = false;
            }
        }
    }
}
