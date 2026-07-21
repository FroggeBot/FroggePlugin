using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Objects.SubKinds;
using FroggePlugin.Api;

namespace FroggePlugin.Windows;

public partial class MainWindow
{
    private VipLoadState vipMembersLoadState = VipLoadState.Idle;
    private string? vipMembersErrorMessage;
    private List<PluginVipMemberSummary>? vipMembers;

    private int selectedVipMemberId;
    private VipLoadState vipMemberDetailLoadState = VipLoadState.Idle;
    private string? vipMemberDetailErrorMessage;
    private PluginVipMemberDetail? vipMemberDetail;

    // Shared by both the member-detail "change tier" flow and the assign-via-target flow - same
    // venue's tier list either way, refetched fresh whenever either screen requests it.
    private VipLoadState vipTiersLoadState = VipLoadState.Idle;
    private string? vipTiersErrorMessage;
    private List<PluginVipTierSummary>? vipTiers;
    private bool showingTierPicker;

    // Guards an assign/remove completion against firing after the user has navigated away -
    // same pattern as Manage.cs's approvalActionSequence.
    private int vipActionSequence;
    private bool vipActionInProgress;
    private string? vipActionErrorMessage;

    private VipLoadState resolveState = VipLoadState.Idle;
    private string? resolveErrorMessage;
    private ulong? resolvedDiscordUserId;
    private string? resolvedCharacterName;

    private void DrawManageVipRoster()
    {
        if (DrawBackButton())
        {
            page = Page.ManageVenue;
            vipMembersLoadState = VipLoadState.Idle;
            vipMembers = null;
            vipMembersErrorMessage = null;
            return;
        }

        ImGui.Separator();
        ImGui.Spacing();
        DrawTitle($"{selectedManageGuildName} - VIP Members");
        ImGui.Spacing();

        if (ColoredButton("Assign New (via Target)", AccentColor, FullWidthButton))
            StartManageVipAssignTarget();
        ImGui.Spacing();

        switch (vipMembersLoadState)
        {
            case VipLoadState.Loading:
                DrawLoading();
                break;

            case VipLoadState.Error:
                DrawError(vipMembersErrorMessage, StartManageVipRoster);
                break;

            case VipLoadState.Loaded:
                if (vipMembers is null || vipMembers.Count == 0)
                {
                    DrawEmpty("No VIP members yet.");
                    break;
                }

                foreach (var member in vipMembers)
                {
                    BeginCard();
                    DrawTitle(member.DisplayName);
                    ImGui.TextDisabled($"{member.TierName} - Expires: {member.ExpiresAt?.ToString("d") ?? "Never"}");
                    ImGui.Spacing();
                    if (ImGui.Button($"View##{member.Id}"))
                        StartManageVipMemberDetail(member.Id);
                    EndCard(AccentColor);
                }
                break;
        }
    }

    private void StartManageVipRoster()
    {
        page = Page.ManageVipRoster;
        vipMembersLoadState = VipLoadState.Loading;
        vipMembersErrorMessage = null;
        _ = LoadAsync(
            () => plugin.ApiClient.GetVipMembersForManageAsync(selectedManageGuildId),
            result => vipMembers = result,
            (state, msg) => { vipMembersLoadState = state; vipMembersErrorMessage = msg; },
            "Couldn't load VIP members"
        );
    }

    private void DrawManageVipMemberDetail()
    {
        if (DrawBackButton())
        {
            page = Page.ManageVipRoster;
            vipMemberDetailLoadState = VipLoadState.Idle;
            vipMemberDetail = null;
            vipMemberDetailErrorMessage = null;
            showingTierPicker = false;
            vipActionInProgress = false;
            vipActionErrorMessage = null;
            vipActionSequence++;
            return;
        }

        ImGui.Separator();
        ImGui.Spacing();

        switch (vipMemberDetailLoadState)
        {
            case VipLoadState.Loading:
                DrawLoading();
                break;

            case VipLoadState.Error:
                DrawError(vipMemberDetailErrorMessage, () => StartManageVipMemberDetail(selectedVipMemberId));
                break;

            case VipLoadState.Loaded:
                if (vipMemberDetail is null)
                    break;

                var m = vipMemberDetail;
                BeginCard();
                DrawTitle(m.DisplayName);
                ImGui.Spacing();
                DrawInlineField("Tier", m.TierName);
                DrawInlineField("Expires", m.ExpiresAt?.ToString("d") ?? "Never");
                DrawInlineField("Notes", m.Notes);
                EndCard(AccentColor);

                ImGui.Spacing();
                if (vipActionErrorMessage is not null)
                    DrawColored(vipActionErrorMessage, DangerColor);

                ImGui.BeginDisabled(vipActionInProgress);
                if (ColoredButton("Change Tier", AccentColor))
                    StartChangeTierPicker();
                ImGui.SameLine();
                if (ColoredButton("Remove", DangerColor))
                    StartRemoveVipMember();
                ImGui.EndDisabled();

                if (showingTierPicker)
                {
                    ImGui.Spacing();
                    DrawSectionHeader("Pick a Tier");
                    DrawTierPicker(StartChangeTierPicker, (tierId, _) => StartAssign(m.DiscordUserId, tierId));
                }
                break;
        }
    }

    private void StartManageVipMemberDetail(int memberId)
    {
        page = Page.ManageVipMemberDetail;
        selectedVipMemberId = memberId;
        vipMemberDetailLoadState = VipLoadState.Loading;
        vipMemberDetailErrorMessage = null;
        showingTierPicker = false;
        vipActionInProgress = false;
        vipActionErrorMessage = null;
        vipActionSequence++;
        _ = LoadAsync(
            () => plugin.ApiClient.GetVipMemberDetailAsync(selectedManageGuildId, memberId),
            result => vipMemberDetail = result,
            (state, msg) => { vipMemberDetailLoadState = state; vipMemberDetailErrorMessage = msg; },
            "Couldn't load member"
        );
    }

    private void StartChangeTierPicker()
    {
        showingTierPicker = true;
        StartLoadTiers();
    }

    private void StartLoadTiers()
    {
        vipTiersLoadState = VipLoadState.Loading;
        vipTiersErrorMessage = null;
        _ = LoadAsync(
            () => plugin.ApiClient.GetVipTiersForManageAsync(selectedManageGuildId),
            result => vipTiers = result,
            (state, msg) => { vipTiersLoadState = state; vipTiersErrorMessage = msg; },
            "Couldn't load tiers"
        );
    }

    private void StartAssign(ulong discordUserId, int tierId)
    {
        vipActionSequence++;
        var mySequence = vipActionSequence;
        vipActionInProgress = true;
        vipActionErrorMessage = null;
        _ = PerformAssignAsync(mySequence, discordUserId, tierId);
    }

    private async Task PerformAssignAsync(int mySequence, ulong discordUserId, int tierId)
    {
        try
        {
            var result = await plugin.ApiClient.AssignVipMemberAsync(selectedManageGuildId, discordUserId, tierId);
            if (mySequence != vipActionSequence)
                return; // navigated away - this completion is abandoned, don't touch shared state

            if (result is null)
            {
                vipActionErrorMessage = "That action failed. Try again.";
                vipActionInProgress = false;
                return;
            }

            // The member/roster data is now stale either way (new assignment or a changed tier) -
            // land back on the roster, which is the natural next screen and re-fetches fresh.
            vipActionInProgress = false;
            vipMembers = null;
            StartManageVipRoster();
        }
        catch (Exception)
        {
            if (mySequence == vipActionSequence)
            {
                vipActionErrorMessage = "That action failed. Try again.";
                vipActionInProgress = false;
            }
        }
    }

    private void StartRemoveVipMember()
    {
        vipActionSequence++;
        var mySequence = vipActionSequence;
        vipActionInProgress = true;
        vipActionErrorMessage = null;
        _ = PerformRemoveAsync(mySequence, selectedVipMemberId);
    }

    private async Task PerformRemoveAsync(int mySequence, int memberId)
    {
        try
        {
            var success = await plugin.ApiClient.RemoveVipMemberAsync(selectedManageGuildId, memberId);
            if (mySequence != vipActionSequence)
                return;

            if (!success)
            {
                vipActionErrorMessage = "That action failed. Try again.";
                vipActionInProgress = false;
                return;
            }

            vipActionInProgress = false;
            vipMembers = null;
            StartManageVipRoster();
        }
        catch (Exception)
        {
            if (mySequence == vipActionSequence)
            {
                vipActionErrorMessage = "That action failed. Try again.";
                vipActionInProgress = false;
            }
        }
    }

    private void DrawManageVipAssignTarget()
    {
        if (DrawBackButton())
        {
            page = Page.ManageVipRoster;
            resolveState = VipLoadState.Idle;
            resolveErrorMessage = null;
            resolvedDiscordUserId = null;
            resolvedCharacterName = null;
            showingTierPicker = false;
            vipActionInProgress = false;
            vipActionErrorMessage = null;
            vipActionSequence++;
            return;
        }

        ImGui.Separator();
        ImGui.Spacing();
        DrawTitle("Assign New Member");
        ImGui.Spacing();
        ImGui.TextWrapped(
            "Target a player character in-game, then use their currently-targeted character to " +
            "look up the Discord account it's linked to via /verify."
        );
        ImGui.Spacing();

        var resolving = resolveState == VipLoadState.Loading;
        ImGui.BeginDisabled(resolving);
        if (ImGui.Button("Use Current Target"))
            StartResolveTarget();
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

                if (vipActionErrorMessage is not null)
                    DrawColored(vipActionErrorMessage, DangerColor);

                DrawSectionHeader("Pick a Tier");
                ImGui.BeginDisabled(vipActionInProgress);
                DrawTierPicker(StartLoadTiers, (tierId, _) => StartAssign(discordUserId, tierId));
                ImGui.EndDisabled();
                break;
        }
    }

    private void StartManageVipAssignTarget()
    {
        page = Page.ManageVipAssignTarget;
        resolveState = VipLoadState.Idle;
        resolveErrorMessage = null;
        resolvedDiscordUserId = null;
        resolvedCharacterName = null;
        showingTierPicker = false;
        vipActionInProgress = false;
        vipActionErrorMessage = null;
    }

    private void StartResolveTarget()
    {
        resolveState = VipLoadState.Loading;
        resolveErrorMessage = null;
        resolvedDiscordUserId = null;
        _ = ResolveTargetAsync();
    }

    // Reads the current target BEFORE the first await, deliberately - Dalamud's game-state
    // services (ITargetManager, IGameObject, etc.) are only safe to touch from the main/
    // framework thread, and the synchronous portion of an async method up to its first `await`
    // still runs on the caller's thread (here, the render thread that called StartResolveTarget
    // from Draw()). Everything after the API call's `await` runs on whatever thread the
    // continuation resumes on, which is fine since it only touches plain plugin state by then.
    private async Task ResolveTargetAsync()
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
            StartLoadTiers();
        }
        catch (Exception ex)
        {
            resolveState = VipLoadState.Error;
            resolveErrorMessage = $"Couldn't resolve target: {ex.Message}";
        }
    }
}
