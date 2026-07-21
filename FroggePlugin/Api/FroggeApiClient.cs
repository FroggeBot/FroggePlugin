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

public sealed record PluginEventGuild(ulong GuildId, string GuildName);

public sealed record PluginEventSummary(int Id, ulong GuildId, string Name, string? Description, DateTimeOffset StartAt, DateTimeOffset? EndAt, string? ImageUrl);

public sealed record PluginShiftDetail(int Id, DateTimeOffset StartAt, DateTimeOffset EndAt, int Capacity, int SignupCount, bool IsSignedUp, bool IsLocked);

public sealed record PluginPositionDetail(string PositionName, List<PluginShiftDetail> Shifts);

public sealed record PluginEventDetail(int Id, ulong GuildId, string Name, string? Description, DateTimeOffset StartAt, DateTimeOffset? EndAt, string? ImageUrl, List<PluginPositionDetail> Positions);

public sealed record PluginGuild(ulong GuildId, string GuildName, bool IsManager);

public sealed record PluginGiveawaySummary(
    int Id,
    ulong GuildId,
    string? Name,
    string? Prize,
    DateTimeOffset? EndAt,
    int EntrantCount,
    bool IsEntered,
    bool IsRolled,
    DateTimeOffset? RolledAt,
    bool IsWinner,
    int WinnerCount,
    string? DiscordLink
);

public sealed record PluginRaffleSummary(
    int Id,
    ulong GuildId,
    string? Name,
    int CostPerTicket,
    int WinnerPct,
    int EntrantCount,
    int TotalTickets,
    int MyTicketCount,
    bool IsRolled,
    DateTimeOffset? RolledAt,
    bool IsWinner,
    int WinnerCount,
    string? DiscordLink
);

public sealed record PluginProfileSummary(int Id, ulong GuildId, string GuildName, string CharacterName, bool IsPrimary, string ApprovalStatus, string? ThumbnailUrl);

public sealed record PluginProfileImage(string ImageUrl, string? Caption);

public sealed record PluginProfileDetail(
    int Id,
    ulong GuildId,
    string GuildName,
    string CharacterName,
    bool IsPrimary,
    string ApprovalStatus,
    string? RejectionReason,
    int? Color,
    string? Jobs,
    string? Rates,
    string? ThumbnailUrl,
    string? MainImageUrl,
    string? World,
    string? DataCenter,
    string? Gender,
    string? Pronouns,
    string? Race,
    string? Clan,
    string? Orientation,
    string? Height,
    string? Age,
    string? MareCode,
    string? Likes,
    string? Dislikes,
    string? Personality,
    string? AboutMe,
    List<PluginProfileImage> AdditionalImages
);

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

    // BaseAddress has no lifecycle restriction on HttpClient (unlike some other properties) -
    // safe to reassign at any time, even after requests have already been sent.
    public void SetApiBaseUrl(string url) => httpClient.BaseAddress = new Uri(url);

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

    public async Task<List<PluginEventGuild>?> GetEventGuildsAsync()
    {
        using var response = await httpClient.GetAsync("/plugin/events/guilds");
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }
        return await response.Content.ReadFromJsonAsync<List<PluginEventGuild>>(JsonOptions);
    }

    public async Task<List<PluginEventSummary>?> GetUpcomingEventsAsync(ulong guildId)
    {
        using var response = await httpClient.GetAsync($"/plugin/events?guild_id={guildId}");
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }
        return await response.Content.ReadFromJsonAsync<List<PluginEventSummary>>(JsonOptions);
    }

    public async Task<PluginEventDetail?> GetEventDetailAsync(ulong guildId, int eventId)
    {
        using var response = await httpClient.GetAsync($"/plugin/events/{eventId}?guild_id={guildId}");
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }
        return await response.Content.ReadFromJsonAsync<PluginEventDetail>(JsonOptions);
    }

    public async Task<bool> SignupForShiftAsync(ulong guildId, int shiftId)
    {
        using var response = await httpClient.PostAsync($"/plugin/events/shifts/{shiftId}/signup?guild_id={guildId}", null);
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> LeaveShiftAsync(ulong guildId, int shiftId)
    {
        using var response = await httpClient.DeleteAsync($"/plugin/events/shifts/{shiftId}/signup?guild_id={guildId}");
        return response.IsSuccessStatusCode;
    }

    public async Task<List<PluginGuild>?> GetGuildsAsync()
    {
        using var response = await httpClient.GetAsync("/plugin/guilds");
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }
        return await response.Content.ReadFromJsonAsync<List<PluginGuild>>(JsonOptions);
    }

    public async Task<List<PluginGiveawaySummary>?> GetOpenGiveawaysAsync(ulong guildId)
    {
        using var response = await httpClient.GetAsync($"/plugin/giveaways?guild_id={guildId}");
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }
        return await response.Content.ReadFromJsonAsync<List<PluginGiveawaySummary>>(JsonOptions);
    }

    public async Task<List<PluginGiveawaySummary>?> GetConcludedGiveawaysAsync(ulong guildId)
    {
        using var response = await httpClient.GetAsync($"/plugin/giveaways/concluded?guild_id={guildId}");
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }
        return await response.Content.ReadFromJsonAsync<List<PluginGiveawaySummary>>(JsonOptions);
    }

    public async Task<List<PluginRaffleSummary>?> GetOpenRafflesAsync(ulong guildId)
    {
        using var response = await httpClient.GetAsync($"/plugin/raffles?guild_id={guildId}");
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }
        return await response.Content.ReadFromJsonAsync<List<PluginRaffleSummary>>(JsonOptions);
    }

    public async Task<List<PluginRaffleSummary>?> GetConcludedRafflesAsync(ulong guildId)
    {
        using var response = await httpClient.GetAsync($"/plugin/raffles/concluded?guild_id={guildId}");
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }
        return await response.Content.ReadFromJsonAsync<List<PluginRaffleSummary>>(JsonOptions);
    }

    public async Task<List<PluginProfileSummary>?> GetProfilesAsync()
    {
        using var response = await httpClient.GetAsync("/plugin/profiles");
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }
        return await response.Content.ReadFromJsonAsync<List<PluginProfileSummary>>(JsonOptions);
    }

    public async Task<PluginProfileDetail?> GetProfileDetailAsync(int characterId)
    {
        using var response = await httpClient.GetAsync($"/plugin/profiles/{characterId}");
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }
        return await response.Content.ReadFromJsonAsync<PluginProfileDetail>(JsonOptions);
    }

    public async Task<List<PluginProfileSummary>?> GetPendingProfileApprovalsAsync(ulong guildId)
    {
        using var response = await httpClient.GetAsync($"/plugin/manage/profiles/pending?guild_id={guildId}");
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }
        return await response.Content.ReadFromJsonAsync<List<PluginProfileSummary>>(JsonOptions);
    }

    public async Task<PluginProfileDetail?> GetProfileApprovalDetailAsync(ulong guildId, int characterId)
    {
        using var response = await httpClient.GetAsync($"/plugin/manage/profiles/{characterId}?guild_id={guildId}");
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }
        return await response.Content.ReadFromJsonAsync<PluginProfileDetail>(JsonOptions);
    }

    public async Task<bool> ApproveProfileAsync(ulong guildId, int characterId)
    {
        using var response = await httpClient.PostAsync(
            $"/plugin/manage/profiles/{characterId}/approve?guild_id={guildId}", null
        );
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> RejectProfileAsync(ulong guildId, int characterId, string reason)
    {
        using var response = await httpClient.PostAsJsonAsync(
            $"/plugin/manage/profiles/{characterId}/reject?guild_id={guildId}", new { reason }, JsonOptions
        );
        return response.IsSuccessStatusCode;
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
