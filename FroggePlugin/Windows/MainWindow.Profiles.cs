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

    private async Task FetchProfilesAsync()
    {
        try
        {
            var result = await plugin.ApiClient.GetProfilesAsync();
            if (result is null)
            {
                profilesErrorMessage = "Couldn't load characters.";
                profilesLoadState = VipLoadState.Error;
                return;
            }

            profiles = result;
            profilesLoadState = VipLoadState.Loaded;
        }
        catch (Exception ex)
        {
            profilesErrorMessage = $"Couldn't load characters: {ex.Message}";
            profilesLoadState = VipLoadState.Error;
        }
    }

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
                var statusColor = ApprovalStatusColor(p.ApprovalStatus);

                BeginCard();

                DrawTitle(p.CharacterName);
                if (p.IsPrimary)
                {
                    ImGui.SameLine();
                    DrawBadge("Primary", AccentColor);
                }
                ImGui.SameLine();
                DrawBadge(p.ApprovalStatus, statusColor);
                ImGui.TextDisabled(p.GuildName);
                if (p.RejectionReason is not null)
                {
                    ImGui.Spacing();
                    DrawColored($"Rejection reason: {p.RejectionReason}", DangerColor);
                }

                var hasMainInfo = !string.IsNullOrEmpty(p.Jobs) || !string.IsNullOrEmpty(p.Rates);
                if (hasMainInfo)
                {
                    DrawSectionHeader("Main Info");
                    if (ImGui.BeginTable("##maininfo", 2, ImGuiTableFlags.SizingStretchSame))
                    {
                        DrawFieldRow("Jobs", p.Jobs, "Rates", p.Rates);
                        ImGui.EndTable();
                    }
                }

                var hasGlance = !string.IsNullOrEmpty(p.Race) || !string.IsNullOrEmpty(p.Clan)
                    || !string.IsNullOrEmpty(p.Gender) || !string.IsNullOrEmpty(p.Pronouns)
                    || !string.IsNullOrEmpty(p.Orientation) || !string.IsNullOrEmpty(p.World)
                    || !string.IsNullOrEmpty(p.DataCenter) || !string.IsNullOrEmpty(p.Height)
                    || !string.IsNullOrEmpty(p.Age) || !string.IsNullOrEmpty(p.MareCode);
                if (hasGlance)
                {
                    DrawSectionHeader("At A Glance");
                    if (ImGui.BeginTable("##glance", 2, ImGuiTableFlags.SizingStretchSame))
                    {
                        DrawFieldRow("Race", p.Race, "Clan", p.Clan);
                        DrawFieldRow("Gender", p.Gender, "Pronouns", p.Pronouns);
                        DrawFieldRow("Orientation", p.Orientation, "World", p.World);
                        DrawFieldRow("Data Center", p.DataCenter, "Height", p.Height);
                        DrawFieldRow("Age", p.Age, "Mare Code", p.MareCode);
                        ImGui.EndTable();
                    }
                }

                var hasNarrativeFields = !string.IsNullOrEmpty(p.Likes) || !string.IsNullOrEmpty(p.Dislikes)
                    || !string.IsNullOrEmpty(p.Personality) || !string.IsNullOrEmpty(p.AboutMe);
                if (hasNarrativeFields)
                {
                    DrawSectionHeader("Personality");
                    DrawLongField("Likes", p.Likes);
                    DrawLongField("Dislikes", p.Dislikes);
                    DrawLongField("Personality", p.Personality);
                    DrawLongField("About Me", p.AboutMe);
                }

                if (p.ThumbnailUrl is not null || p.MainImageUrl is not null || p.AdditionalImages.Count > 0)
                {
                    // Actual image rendering (fetching + texture-uploading a remote URL) is a
                    // real separate capability this plugin doesn't have yet - deliberately out
                    // of scope for a styling pass, same as event image_url. URLs stay as text.
                    DrawSectionHeader("Images");
                    if (p.ThumbnailUrl is not null)
                        DrawInlineField("Thumbnail", p.ThumbnailUrl);
                    if (p.MainImageUrl is not null)
                        DrawInlineField("Main Image", p.MainImageUrl);
                    foreach (var image in p.AdditionalImages)
                        DrawInlineField(image.Caption ?? "Image", image.ImageUrl);
                }

                EndCard(statusColor);
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

    private async Task FetchProfileDetailAsync(int characterId)
    {
        try
        {
            var result = await plugin.ApiClient.GetProfileDetailAsync(characterId);
            if (result is null)
            {
                profileDetailErrorMessage = "Couldn't load character.";
                profileDetailLoadState = VipLoadState.Error;
                return;
            }

            profileDetail = result;
            profileDetailLoadState = VipLoadState.Loaded;
        }
        catch (Exception ex)
        {
            profileDetailErrorMessage = $"Couldn't load character: {ex.Message}";
            profileDetailLoadState = VipLoadState.Error;
        }
    }
}
