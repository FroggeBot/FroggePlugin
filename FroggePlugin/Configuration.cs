using Dalamud.Configuration;
using System;

namespace FroggePlugin;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 0;

    public string ApiBaseUrl { get; set; } = "http://127.0.0.1:8000";

    public string? AuthToken { get; set; }
    public ulong? LinkedDiscordUserId { get; set; }
    public string? LinkedDiscordUsername { get; set; }

    public void Save()
    {
        Plugin.PluginInterface.SavePluginConfig(this);
    }
}
