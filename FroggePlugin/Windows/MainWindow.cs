using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace FroggePlugin.Windows;

public class MainWindow : Window, IDisposable
{
    private readonly Plugin plugin;

    public MainWindow(Plugin plugin) : base("FroggePlugin##MainWindow")
    {
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(375, 200),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
        };

        this.plugin = plugin;
    }

    public void Dispose() { }

    public override void Draw()
    {
        ImGui.Text("FroggePlugin");
        ImGui.Separator();
        ImGui.TextWrapped($"FroggeAPI base URL: {plugin.Configuration.ApiBaseUrl}");
        ImGui.TextWrapped("No features are wired up yet — this is a bare project scaffold.");
    }
}
