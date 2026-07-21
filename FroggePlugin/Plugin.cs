using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using FroggePlugin.Api;
using FroggePlugin.Windows;

namespace FroggePlugin;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static ITargetManager TargetManager { get; private set; } = null!;

    private const string CommandName = "/frogge";

    public Configuration Configuration { get; init; }

    public readonly WindowSystem WindowSystem = new("FroggePlugin");
    private MainWindow MainWindow { get; init; }
    internal FroggeApiClient ApiClient { get; init; }

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        // Early testing builds defaulted ApiBaseUrl to a local dev address - a saved config from
        // one of those still has that value on disk and won't pick up Configuration's own default
        // just by upgrading, since Dalamud loads whatever was persisted. Self-correct once.
        if (Configuration.ApiBaseUrl == "http://127.0.0.1:8000")
        {
            Configuration.ApiBaseUrl = "https://api.frogge.tech";
            Configuration.Save();
        }

        ApiClient = new FroggeApiClient(Configuration);
        ApiClient.SetAuthToken(Configuration.AuthToken);

        MainWindow = new MainWindow(this);
        WindowSystem.AddWindow(MainWindow);

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Opens the Frogge window."
        });

        PluginInterface.UiBuilder.Draw += WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenMainUi += ToggleMainUi;
        PluginInterface.UiBuilder.OpenConfigUi += OpenConfigUi;
    }

    public void Dispose()
    {
        PluginInterface.UiBuilder.Draw -= WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleMainUi;
        PluginInterface.UiBuilder.OpenConfigUi -= OpenConfigUi;

        WindowSystem.RemoveAllWindows();

        MainWindow.Dispose();
        ApiClient.Dispose();

        CommandManager.RemoveHandler(CommandName);
    }

    private void OnCommand(string command, string args) => MainWindow.Toggle();

    public void ToggleMainUi() => MainWindow.Toggle();

    private void OpenConfigUi() => MainWindow.OpenSettings();
}
