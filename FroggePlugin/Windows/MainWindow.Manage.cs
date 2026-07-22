using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using FroggePlugin.Api;

namespace FroggePlugin.Windows;

public partial class MainWindow
{
    // Which venue's manager tools are being used - set when a managed-venue button is clicked
    // in DrawManage.
    private ulong selectedManageGuildId;
    private string selectedManageGuildName = string.Empty;

    private VipLoadState pendingApprovalsLoadState = VipLoadState.Idle;
    private string? pendingApprovalsErrorMessage;
    private List<PluginProfileSummary>? pendingApprovals;

    private int selectedApprovalCharacterId;
    private VipLoadState approvalDetailLoadState = VipLoadState.Idle;
    private string? approvalDetailErrorMessage;
    private PluginProfileDetail? approvalDetail;

    // Guards an approve/reject completion against firing after the user has already navigated
    // away from this character (e.g. clicked Back or opened a different review mid-request) -
    // same pattern as Events' shift-action actionSequence guard.
    private int approvalActionSequence;
    private bool approvalActionInProgress;
    private string? approvalActionErrorMessage;
    private string rejectReasonInput = string.Empty;

    private void DrawManage()
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
                DrawError(guildsErrorMessage, StartManage);
                break;

            case VipLoadState.Loaded:
                var managed = guilds?.Where(g => g.IsManager).ToList() ?? [];
                if (managed.Count == 0)
                {
                    DrawEmpty("You don't manage any linked venues.");
                    break;
                }

                foreach (var guild in managed)
                {
                    if (ColoredButton($"{guild.GuildName}##{guild.GuildId}", AccentColor, FullWidthButton))
                        StartManageVenue(guild.GuildId, guild.GuildName);
                    ImGui.Spacing();
                }
                break;
        }
    }

    private void StartManage()
    {
        page = Page.Manage;
        guildsLoadState = VipLoadState.Loading;
        guildsErrorMessage = null;
        _ = FetchGuildsAsync();
    }

    private void DrawManageVenue()
    {
        if (DrawBackButton())
        {
            page = Page.Manage;
            return;
        }

        ImGui.Separator();
        ImGui.Spacing();
        DrawTitle(selectedManageGuildName);
        ImGui.Spacing();

        if (ColoredButton("Profile Approvals", AccentColor, FullWidthButton))
            StartProfileApprovalQueue();
        ImGui.Spacing();

        if (ColoredButton("VIP Members", AccentColor, FullWidthButton))
            StartManageVipRoster();
        ImGui.Spacing();

        if (ColoredButton("Giveaways", AccentColor, FullWidthButton))
            StartManageGiveawayList();
        ImGui.Spacing();

        if (ColoredButton("Raffles", AccentColor, FullWidthButton))
            StartManageRaffleList();
        ImGui.Spacing();

        if (ColoredButton("Staffing", AccentColor, FullWidthButton))
            StartManageStaffingRoster();
    }

    private void StartManageVenue(ulong guildId, string guildName)
    {
        page = Page.ManageVenue;
        selectedManageGuildId = guildId;
        selectedManageGuildName = guildName;
    }

    private void DrawProfileApprovalQueue()
    {
        if (DrawBackButton())
        {
            page = Page.ManageVenue;
            pendingApprovalsLoadState = VipLoadState.Idle;
            pendingApprovals = null;
            pendingApprovalsErrorMessage = null;
            return;
        }

        ImGui.Separator();
        ImGui.Spacing();
        DrawTitle($"{selectedManageGuildName} - Profile Approvals");
        ImGui.Spacing();

        switch (pendingApprovalsLoadState)
        {
            case VipLoadState.Loading:
                DrawLoading();
                break;

            case VipLoadState.Error:
                DrawError(pendingApprovalsErrorMessage, StartProfileApprovalQueue);
                break;

            case VipLoadState.Loaded:
                if (pendingApprovals is null || pendingApprovals.Count == 0)
                {
                    DrawEmpty("Nothing pending review.");
                    break;
                }

                foreach (var profile in pendingApprovals)
                {
                    BeginCard();
                    DrawTitle(profile.CharacterName);
                    ImGui.Spacing();
                    if (ImGui.Button($"Review##{profile.Id}"))
                        StartProfileApprovalDetail(profile.Id);
                    EndCard(WarningColor);
                }
                break;
        }
    }

    private void StartProfileApprovalQueue()
    {
        page = Page.ProfileApprovalQueue;
        pendingApprovalsLoadState = VipLoadState.Loading;
        pendingApprovalsErrorMessage = null;
        _ = FetchPendingApprovalsAsync();
    }

    private async Task FetchPendingApprovalsAsync()
    {
        try
        {
            var result = await plugin.ApiClient.GetPendingProfileApprovalsAsync(selectedManageGuildId);
            if (result is null)
            {
                pendingApprovalsErrorMessage = "Couldn't load pending approvals.";
                pendingApprovalsLoadState = VipLoadState.Error;
                return;
            }

            pendingApprovals = result;
            pendingApprovalsLoadState = VipLoadState.Loaded;
        }
        catch (Exception ex)
        {
            pendingApprovalsErrorMessage = $"Couldn't load pending approvals: {ex.Message}";
            pendingApprovalsLoadState = VipLoadState.Error;
        }
    }

    private void DrawProfileApprovalDetail()
    {
        if (DrawBackButton())
        {
            page = Page.ProfileApprovalQueue;
            approvalDetailLoadState = VipLoadState.Idle;
            approvalDetail = null;
            approvalDetailErrorMessage = null;
            approvalActionInProgress = false;
            approvalActionErrorMessage = null;
            rejectReasonInput = string.Empty;
            approvalActionSequence++; // invalidate any in-flight approve/reject tied to this view
            return;
        }

        ImGui.Separator();
        ImGui.Spacing();

        switch (approvalDetailLoadState)
        {
            case VipLoadState.Loading:
                DrawLoading();
                break;

            case VipLoadState.Error:
                DrawError(approvalDetailErrorMessage, () => StartProfileApprovalDetail(selectedApprovalCharacterId));
                break;

            case VipLoadState.Loaded:
                if (approvalDetail is null)
                    break;

                var p = approvalDetail;
                BeginCard();
                DrawProfileContent(p);
                ImGui.Spacing();

                if (approvalActionErrorMessage is not null)
                    DrawColored(approvalActionErrorMessage, DangerColor);

                ImGui.BeginDisabled(approvalActionInProgress);
                if (ColoredButton("Approve", SuccessColor))
                    StartApproveAction();
                ImGui.EndDisabled();

                ImGui.Spacing();
                ImGui.SetNextItemWidth(-1);
                ImGui.InputText("##RejectReason", ref rejectReasonInput, 300);
                ImGui.BeginDisabled(approvalActionInProgress || string.IsNullOrWhiteSpace(rejectReasonInput));
                if (ColoredButton("Reject", DangerColor))
                    StartRejectAction();
                ImGui.EndDisabled();

                EndCard(ApprovalStatusColor(p.ApprovalStatus), leftAccentStripe: true);
                break;
        }
    }

    private void StartProfileApprovalDetail(int characterId)
    {
        page = Page.ProfileApprovalDetail;
        selectedApprovalCharacterId = characterId;
        approvalDetailLoadState = VipLoadState.Loading;
        approvalDetailErrorMessage = null;
        approvalActionInProgress = false;
        approvalActionErrorMessage = null;
        rejectReasonInput = string.Empty;
        approvalActionSequence++; // invalidate any in-flight approve/reject from a previous visit
        _ = FetchApprovalDetailAsync(characterId);
    }

    private async Task FetchApprovalDetailAsync(int characterId)
    {
        try
        {
            var result = await plugin.ApiClient.GetProfileApprovalDetailAsync(selectedManageGuildId, characterId);
            if (result is null)
            {
                approvalDetailErrorMessage = "Couldn't load character.";
                approvalDetailLoadState = VipLoadState.Error;
                return;
            }

            approvalDetail = result;
            approvalDetailLoadState = VipLoadState.Loaded;
        }
        catch (Exception ex)
        {
            approvalDetailErrorMessage = $"Couldn't load character: {ex.Message}";
            approvalDetailLoadState = VipLoadState.Error;
        }
    }

    private void StartApproveAction()
    {
        approvalActionSequence++;
        var mySequence = approvalActionSequence;
        approvalActionInProgress = true;
        approvalActionErrorMessage = null;
        _ = PerformApproveAsync(mySequence);
    }

    private async Task PerformApproveAsync(int mySequence)
    {
        try
        {
            var success = await plugin.ApiClient.ApproveProfileAsync(selectedManageGuildId, selectedApprovalCharacterId);
            if (mySequence != approvalActionSequence)
                return; // navigated away - this completion is abandoned, don't touch shared state

            if (!success)
            {
                approvalActionErrorMessage = "That action failed. Try again.";
                approvalActionInProgress = false;
                return;
            }

            // Approved characters drop out of the pending queue - go back to it rather than
            // re-fetching this same character's now-stale "pending" detail.
            approvalActionInProgress = false;
            pendingApprovals = null;
            StartProfileApprovalQueue();
        }
        catch (Exception)
        {
            if (mySequence == approvalActionSequence)
            {
                approvalActionErrorMessage = "That action failed. Try again.";
                approvalActionInProgress = false;
            }
        }
    }

    private void StartRejectAction()
    {
        approvalActionSequence++;
        var mySequence = approvalActionSequence;
        approvalActionInProgress = true;
        approvalActionErrorMessage = null;
        _ = PerformRejectAsync(mySequence, rejectReasonInput);
    }

    private async Task PerformRejectAsync(int mySequence, string reason)
    {
        try
        {
            var success = await plugin.ApiClient.RejectProfileAsync(selectedManageGuildId, selectedApprovalCharacterId, reason);
            if (mySequence != approvalActionSequence)
                return;

            if (!success)
            {
                approvalActionErrorMessage = "That action failed. Try again.";
                approvalActionInProgress = false;
                return;
            }

            approvalActionInProgress = false;
            pendingApprovals = null;
            StartProfileApprovalQueue();
        }
        catch (Exception)
        {
            if (mySequence == approvalActionSequence)
            {
                approvalActionErrorMessage = "That action failed. Try again.";
                approvalActionInProgress = false;
            }
        }
    }
}
