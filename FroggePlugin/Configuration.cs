using Dalamud.Configuration;
using System;

namespace FroggePlugin;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 0;

    public string ApiBaseUrl { get; set; } = "https://api.frogge.tech";

    public string? AuthToken { get; set; }
    public ulong? LinkedDiscordUserId { get; set; }
    public string? LinkedDiscordUsername { get; set; }

    public void Save()
    {
        Plugin.PluginInterface.SavePluginConfig(this);
    }
}
