using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;

namespace FroggePlugin.Api;

public sealed record PluginTokenRedeemed(string Token, ulong DiscordUserId, string? DiscordUsername);

public sealed record PluginAuthMe(ulong DiscordUserId, string? DiscordUsername);

public sealed record PluginVipMembership(ulong GuildId, string GuildName, string TierName, DateTimeOffset? ExpiresAt);

public sealed record PluginVipHistoryPeriod(string TierName, DateTimeOffset StartedAt, DateTimeOffset? EndedAt, string? EndedReason);

public sealed record PluginVipPerkStatus(string Text, string RedemptionStatus);

public sealed class FroggeApiClient : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private readonly HttpClient httpClient;

    public FroggeApiClient(Configuration configuration)
    {
        httpClient = new HttpClient
        {
            BaseAddress = new Uri(configuration.ApiBaseUrl),
            Timeout = TimeSpan.FromSeconds(10)
        };
    }

    public void SetAuthToken(string? token)
    {
        httpClient.DefaultRequestHeaders.Authorization =
            token is null ? null : new AuthenticationHeaderValue("Bearer", token);
    }

    public async Task<bool> PingAsync()
    {
        using var response = await httpClient.GetAsync("/health");
        return response.IsSuccessStatusCode;
    }

    public async Task<PluginTokenRedeemed?> RedeemPairingCodeAsync(string code)
    {
        using var response = await httpClient.PostAsJsonAsync("/plugin-auth/redeem", new { code }, JsonOptions);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }
        return await response.Content.ReadFromJsonAsync<PluginTokenRedeemed>(JsonOptions);
    }

    public async Task<PluginAuthMe?> GetMeAsync()
    {
        using var response = await httpClient.GetAsync("/plugin-auth/me");
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }
        return await response.Content.ReadFromJsonAsync<PluginAuthMe>(JsonOptions);
    }

    public async Task<List<PluginVipMembership>?> GetVipMembershipsAsync()
    {
        using var response = await httpClient.GetAsync("/plugin/vip/memberships");
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }
        return await response.Content.ReadFromJsonAsync<List<PluginVipMembership>>(JsonOptions);
    }

    public async Task<List<PluginVipHistoryPeriod>?> GetVipHistoryAsync(ulong guildId)
    {
        using var response = await httpClient.GetAsync($"/plugin/vip/history?guild_id={guildId}");
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }
        return await response.Content.ReadFromJsonAsync<List<PluginVipHistoryPeriod>>(JsonOptions);
    }

    public async Task<List<PluginVipPerkStatus>?> GetVipPerksAsync(ulong guildId)
    {
        using var response = await httpClient.GetAsync($"/plugin/vip/perks?guild_id={guildId}");
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }
        return await response.Content.ReadFromJsonAsync<List<PluginVipPerkStatus>>(JsonOptions);
    }

    public async Task<bool> RevokeAsync()
    {
        try
        {
            using var response = await httpClient.DeleteAsync("/plugin-auth/me");
            return response.IsSuccessStatusCode;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public void Dispose()
    {
        httpClient.Dispose();
    }
}
