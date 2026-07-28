using System;
using Dalamud.Bindings.ImGui;

namespace FroggePlugin.Windows;

public partial class MainWindow
{
    private string apiUrlInput = string.Empty;
    private string? apiUrlError;
    private string? saveMessage;

    private VipLoadState testConnectionState = VipLoadState.Idle;
    private string? testConnectionMessage;

    // Dalamud's own config-UI entry point (Plugin.cs's OpenConfigUi handler, wired to
    // PluginInterface.UiBuilder.OpenConfigUi) - the gear icon in the Plugin Installer's list
    // calls this. Opens the window if it isn't already and jumps straight to Settings, reusing
    // the exact same navigation StartSettings() already does for the in-window button.
    public void OpenSettings()
    {
        IsOpen = true;
        StartSettings();
    }

    private void DrawSettings()
    {
        if (DrawBackButton())
        {
            page = Page.Home;
            testConnectionState = VipLoadState.Idle;
            testConnectionMessage = null;
            saveMessage = null;
            apiUrlError = null;
            return;
        }

        ImGui.Separator();
        ImGui.Spacing();
        DrawTitle("Settings");
        ImGui.Spacing();

        ImGui.TextDisabled("API Server");
        ImGui.SetNextItemWidth(-1);
        ImGui.InputText("##ApiBaseUrl", ref apiUrlInput, 200);
        ImGui.Spacing();

        if (ColoredButton("Save", AccentColor))
            SaveApiBaseUrl();
        ImGui.SameLine();
        if (ColoredButton("Reset to Default", MutedColor))
        {
            apiUrlInput = "https://api.frogge.tech";
            SaveApiBaseUrl();
        }

        if (apiUrlError is not null)
        {
            ImGui.Spacing();
            DrawColored(apiUrlError, DangerColor);
        }
        else if (saveMessage is not null)
        {
            ImGui.Spacing();
            DrawColored(saveMessage, SuccessColor);
        }

        ImGui.Spacing();
        ImGui.Spacing();

        var testing = testConnectionState == VipLoadState.Loading;
        ImGui.BeginDisabled(testing);
        if (ImGui.Button("Test Connection"))
            StartTestConnection();
        ImGui.EndDisabled();

        ImGui.Spacing();
        switch (testConnectionState)
        {
            case VipLoadState.Loading:
                ImGui.TextDisabled("Testing...");
                break;
            case VipLoadState.Loaded:
                DrawColored(testConnectionMessage ?? "Connected.", SuccessColor);
                break;
            case VipLoadState.Error:
                DrawColored(testConnectionMessage ?? "Couldn't connect.", DangerColor);
                break;
        }

        // Only rendered while actually linked - Settings is reachable unlinked too (to fix a bad
        // ApiBaseUrl before a first link), and there's nothing to forget in that state.
        if (plugin.Configuration.AuthToken is not null)
        {
            ImGui.Spacing();
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            if (ColoredButton("Forget Me", DangerColor, FullWidthButton))
            {
                plugin.Configuration.AuthToken = null;
                plugin.Configuration.LinkedDiscordUserId = null;
                plugin.Configuration.LinkedDiscordUsername = null;
                plugin.Configuration.Save();
                plugin.ApiClient.SetAuthToken(null);

                // Best-effort server-side revoke; RevokeAsync swallows its own exceptions, and
                // local state above is already authoritative for the UI regardless of outcome.
                _ = plugin.ApiClient.RevokeAsync();

                // Page.Settings is checked before the AuthToken-null check in Draw(), so without
                // this the player would stay stuck on Settings post-forget instead of landing on
                // the unlinked/link-code screen - mirrors the Back button's own reset above.
                page = Page.Home;
                testConnectionState = VipLoadState.Idle;
                testConnectionMessage = null;
                saveMessage = null;
                apiUrlError = null;
            }
        }
    }

    private void StartSettings()
    {
        page = Page.Settings;
        apiUrlInput = plugin.Configuration.ApiBaseUrl;
        apiUrlError = null;
        saveMessage = null;
        testConnectionState = VipLoadState.Idle;
        testConnectionMessage = null;
    }

    private void SaveApiBaseUrl()
    {
        var trimmed = apiUrlInput.Trim();
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            apiUrlError = "Enter a valid http:// or https:// URL.";
            saveMessage = null;
            return;
        }

        apiUrlInput = uri.ToString().TrimEnd('/');
        plugin.Configuration.ApiBaseUrl = apiUrlInput;
        plugin.Configuration.Save();
        plugin.ApiClient.SetApiBaseUrl(apiUrlInput);

        apiUrlError = null;
        saveMessage = "Saved.";
        testConnectionState = VipLoadState.Idle;
        testConnectionMessage = null;
    }

    private void StartTestConnection()
    {
        testConnectionState = VipLoadState.Loading;
        testConnectionMessage = null;
        _ = TestConnectionAsync();
    }

    private async System.Threading.Tasks.Task TestConnectionAsync()
    {
        try
        {
            var ok = await plugin.ApiClient.PingAsync();
            testConnectionState = ok ? VipLoadState.Loaded : VipLoadState.Error;
            testConnectionMessage = ok ? "Connected." : "Server responded, but not healthy.";
        }
        catch (Exception ex)
        {
            testConnectionState = VipLoadState.Error;
            testConnectionMessage = $"Couldn't connect: {ex.Message}";
        }
    }
}
