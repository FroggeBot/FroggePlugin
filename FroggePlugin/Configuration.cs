using Dalamud.Configuration;
using System;

namespace FroggePlugin;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 0;

    public string ApiBaseUrl { get; set; } = "http://127.0.0.1:8000";

    public void Save()
    {
        Plugin.PluginInterface.SavePluginConfig(this);
    }
}
