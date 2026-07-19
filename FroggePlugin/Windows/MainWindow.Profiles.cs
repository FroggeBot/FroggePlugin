using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using FroggePlugin.Api;

namespace FroggePlugin.Windows;

public partial class MainWindow
{
    private VipLoadState profilesLoadState = VipLoadState.Idle;
    private string? profilesErrorMessage;
    private List<PluginProfileSummary>? profiles;

    private int selectedProfileId;
    private VipLoadState profileDetailLoadState = VipLoadState.Idle;
    private string? profileDetailErrorMessage;
    private PluginProfileDetail? profileDetail;

    private void DrawProfiles()
    {
        if (DrawBackButton())
        {
            page = Page.Home;
            profilesLoadState = VipLoadState.Idle;
            profiles = null;
            profilesErrorMessage = null;
            return;
        }

        ImGui.Separator();
        ImGui.Spacing();

        switch (profilesLoadState)
        {
            case VipLoadState.Loading:
                DrawLoading();
                break;

            case VipLoadState.Error:
                DrawError(profilesErrorMessage, StartProfiles);
                break;

            case VipLoadState.Loaded:
                if (profiles is null || profiles.Count == 0)
                {
                    DrawEmpty("No characters yet.");
                    break;
                }

                foreach (var profile in profiles)
                {
                    var statusColor = ApprovalStatusColor(profile.ApprovalStatus);
                    BeginCard();
                    DrawTitle(profile.CharacterName);
                    if (profile.IsPrimary)
                    {
                        ImGui.SameLine();
                        DrawBadge("Primary", AccentColor);
                    }
                    ImGui.SameLine();
                    DrawBadge(profile.ApprovalStatus, statusColor);

                    ImGui.TextDisabled(profile.GuildName);
                    ImGui.Spacing();
                    if (ImGui.Button($"View##{profile.Id}"))
                        StartProfileDetail(profile.Id);
                    EndCard(statusColor);
                }
                break;
        }
    }

    private void StartProfiles()
    {
        page = Page.Profiles;
        profilesLoadState = VipLoadState.Loading;
        profilesErrorMessage = null;
        _ = FetchProfilesAsync();
    }

    private Task FetchProfilesAsync() => LoadAsync(
        plugin.ApiClient.GetProfilesAsync,
        result => profiles = result,
        (loadState, err) => { profilesLoadState = loadState; if (err != null) profilesErrorMessage = err; },
        "Couldn't load characters");

    private void DrawProfileDetail()
    {
        if (DrawBackButton())
        {
            page = Page.Profiles;
            profileDetailLoadState = VipLoadState.Idle;
            profileDetail = null;
            profileDetailErrorMessage = null;
            return;
        }

        ImGui.Separator();
        ImGui.Spacing();

        switch (profileDetailLoadState)
        {
            case VipLoadState.Loading:
                DrawLoading();
                break;

            case VipLoadState.Error:
                DrawError(profileDetailErrorMessage, () => StartProfileDetail(selectedProfileId));
                break;

            case VipLoadState.Loaded:
                if (profileDetail is null)
                    break;

                var p = profileDetail;
                BeginCard();
                DrawProfileContent(p);
                EndCard(ApprovalStatusColor(p.ApprovalStatus));
                break;
        }
    }

    private void StartProfileDetail(int characterId)
    {
        page = Page.ProfileDetail;
        selectedProfileId = characterId;
        profileDetailLoadState = VipLoadState.Loading;
        profileDetailErrorMessage = null;
        _ = FetchProfileDetailAsync(characterId);
    }

    private Task FetchProfileDetailAsync(int characterId) => LoadAsync(
        () => plugin.ApiClient.GetProfileDetailAsync(characterId),
        result => profileDetail = result,
        (loadState, err) => { profileDetailLoadState = loadState; if (err != null) profileDetailErrorMessage = err; },
        "Couldn't load character");
}
