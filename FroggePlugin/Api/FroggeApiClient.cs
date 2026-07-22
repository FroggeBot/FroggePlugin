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

// Cost is long, not int - the server places no upper bound on a VIP tier's cost, and real dev
// data already has a tier well past Int32.MaxValue (confirmed live, not a guess).
public sealed record PluginVipTierSummary(int Id, string Name, long Cost);

public sealed record PluginVipMemberSummary(int Id, ulong DiscordUserId, string DisplayName, string TierName, DateTimeOffset? ExpiresAt);

public sealed record PluginVipMemberDetail(int Id, ulong DiscordUserId, string DisplayName, int TierId, string TierName, DateTimeOffset? ExpiresAt, string? Notes);

public sealed record PluginResolveCharacterResponse(ulong DiscordUserId, string CharacterName);

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

// Manager-facing summaries carry real winner_ids (a manager legitimately needs to see who won) -
// the attendee-facing summaries above deliberately hide discord_user_id entirely instead.
public sealed record PluginManageGiveawaySummary(
    int Id,
    ulong GuildId,
    string? Name,
    string? Prize,
    DateTimeOffset? EndAt,
    int EntrantCount,
    bool IsRolled,
    DateTimeOffset? RolledAt,
    List<ulong> WinnerIds,
    string? DiscordLink
);

public sealed record PluginManageRaffleSummary(
    int Id,
    ulong GuildId,
    string? Name,
    int CostPerTicket,
    int WinnerPct,
    int EntrantCount,
    int TotalTickets,
    bool IsRolled,
    DateTimeOffset? RolledAt,
    List<ulong> WinnerIds,
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

    private HttpClient httpClient;
    private string? authToken;

    public FroggeApiClient(Configuration configuration)
    {
        httpClient = CreateClient(configuration.ApiBaseUrl);
    }

    private static HttpClient CreateClient(string baseUrl) => new()
    {
        BaseAddress = new Uri(baseUrl),
        Timeout = TimeSpan.FromSeconds(10)
    };

    public void SetAuthToken(string? token)
    {
        authToken = token;
        httpClient.DefaultRequestHeaders.Authorization =
            token is null ? null : new AuthenticationHeaderValue("Bearer", token);
    }

    // HttpClient throws InvalidOperationException on BaseAddress (and several other properties)
    // once it's already sent a request - confirmed live via a real in-game exception, not assumed.
    // The only way to actually change it at runtime is a fresh instance; the old one finishes
    // whatever's already in flight against it and gets disposed once the new one is in place.
    public void SetApiBaseUrl(string url)
    {
        var old = httpClient;
        var next = CreateClient(url);
        next.DefaultRequestHeaders.Authorization =
            authToken is null ? null : new AuthenticationHeaderValue("Bearer", authToken);
        httpClient = next;
        old.Dispose();
    }

    // --- Logging plumbing ---------------------------------------------------------------
    // Every request goes through here so it's always visible in dalamud.log without having
    // to reverse-engineer failures from the outside (server DB, curl, etc.) after the fact -
    // exactly the kind of thing that ate a lot of time before this existed. Logged at
    // Information (not Debug/Verbose), since Dalamud's default log level may filter those out
    // and the whole point is these are reliably visible.

    private static string Truncate(string body, int max = 500) =>
        body.Length <= max ? body : body[..max] + "... (truncated)";

    private async Task<(bool Success, string Body)> SendAsync(HttpMethod method, string url, HttpContent? content = null)
    {
        Plugin.Log.Information($"{method} {url}");
        using var request = new HttpRequestMessage(method, url) { Content = content };
        using var response = await httpClient.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        if (response.IsSuccessStatusCode)
        {
            Plugin.Log.Information($"{method} {url} -> {(int)response.StatusCode}");
        }
        else
        {
            Plugin.Log.Warning($"{method} {url} -> {(int)response.StatusCode} {response.StatusCode}: {Truncate(body)}");
        }

        return (response.IsSuccessStatusCode, body);
    }

    // Deserializes from an already-read string (not directly off the response stream) so the
    // raw body is always available to log, even when deserialization itself fails - that raw
    // body is what actually diagnosed the last two real bugs (an int overflow, a bad URL),
    // versus reflection-guessing on this side alone.
    private static T? Deserialize<T>(string url, string body)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(body, JsonOptions);
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, $"Failed to deserialize response from {url}. Body: {Truncate(body)}");
            throw;
        }
    }

    private async Task<T?> GetJsonAsync<T>(string url)
    {
        var (success, body) = await SendAsync(HttpMethod.Get, url);
        return success ? Deserialize<T>(url, body) : default;
    }

    private async Task<bool> GetOkAsync(string url)
    {
        var (success, _) = await SendAsync(HttpMethod.Get, url);
        return success;
    }

    private async Task<T?> PostJsonAsync<T>(string url, object? payload = null)
    {
        using var content = payload is null ? null : JsonContent.Create(payload, options: JsonOptions);
        var (success, body) = await SendAsync(HttpMethod.Post, url, content);
        return success ? Deserialize<T>(url, body) : default;
    }

    private async Task<bool> PostOkAsync(string url, object? payload = null)
    {
        using var content = payload is null ? null : JsonContent.Create(payload, options: JsonOptions);
        var (success, _) = await SendAsync(HttpMethod.Post, url, content);
        return success;
    }

    private async Task<T?> PutJsonAsync<T>(string url, object payload)
    {
        using var content = JsonContent.Create(payload, options: JsonOptions);
        var (success, body) = await SendAsync(HttpMethod.Put, url, content);
        return success ? Deserialize<T>(url, body) : default;
    }

    private async Task<bool> DeleteOkAsync(string url)
    {
        var (success, _) = await SendAsync(HttpMethod.Delete, url);
        return success;
    }

    // --- Endpoints ------------------------------------------------------------------------

    public Task<bool> PingAsync() => GetOkAsync("/health");

    public Task<PluginTokenRedeemed?> RedeemPairingCodeAsync(string code) =>
        PostJsonAsync<PluginTokenRedeemed>("/plugin-auth/redeem", new { code });

    public Task<PluginAuthMe?> GetMeAsync() => GetJsonAsync<PluginAuthMe>("/plugin-auth/me");

    public Task<List<PluginVipMembership>?> GetVipMembershipsAsync() =>
        GetJsonAsync<List<PluginVipMembership>>("/plugin/vip/memberships");

    public Task<List<PluginVipHistoryPeriod>?> GetVipHistoryAsync(ulong guildId) =>
        GetJsonAsync<List<PluginVipHistoryPeriod>>($"/plugin/vip/history?guild_id={guildId}");

    public Task<List<PluginVipPerkStatus>?> GetVipPerksAsync(ulong guildId) =>
        GetJsonAsync<List<PluginVipPerkStatus>>($"/plugin/vip/perks?guild_id={guildId}");

    public Task<List<PluginEventGuild>?> GetEventGuildsAsync() =>
        GetJsonAsync<List<PluginEventGuild>>("/plugin/events/guilds");

    public Task<List<PluginEventSummary>?> GetUpcomingEventsAsync(ulong guildId) =>
        GetJsonAsync<List<PluginEventSummary>>($"/plugin/events?guild_id={guildId}");

    public Task<PluginEventDetail?> GetEventDetailAsync(ulong guildId, int eventId) =>
        GetJsonAsync<PluginEventDetail>($"/plugin/events/{eventId}?guild_id={guildId}");

    public Task<bool> SignupForShiftAsync(ulong guildId, int shiftId) =>
        PostOkAsync($"/plugin/events/shifts/{shiftId}/signup?guild_id={guildId}");

    public Task<bool> LeaveShiftAsync(ulong guildId, int shiftId) =>
        DeleteOkAsync($"/plugin/events/shifts/{shiftId}/signup?guild_id={guildId}");

    public Task<List<PluginGuild>?> GetGuildsAsync() => GetJsonAsync<List<PluginGuild>>("/plugin/guilds");

    public Task<List<PluginGiveawaySummary>?> GetOpenGiveawaysAsync(ulong guildId) =>
        GetJsonAsync<List<PluginGiveawaySummary>>($"/plugin/giveaways?guild_id={guildId}");

    public Task<List<PluginGiveawaySummary>?> GetConcludedGiveawaysAsync(ulong guildId) =>
        GetJsonAsync<List<PluginGiveawaySummary>>($"/plugin/giveaways/concluded?guild_id={guildId}");

    public Task<List<PluginRaffleSummary>?> GetOpenRafflesAsync(ulong guildId) =>
        GetJsonAsync<List<PluginRaffleSummary>>($"/plugin/raffles?guild_id={guildId}");

    public Task<List<PluginRaffleSummary>?> GetConcludedRafflesAsync(ulong guildId) =>
        GetJsonAsync<List<PluginRaffleSummary>>($"/plugin/raffles/concluded?guild_id={guildId}");

    public Task<List<PluginProfileSummary>?> GetProfilesAsync() =>
        GetJsonAsync<List<PluginProfileSummary>>("/plugin/profiles");

    public Task<PluginProfileDetail?> GetProfileDetailAsync(int characterId) =>
        GetJsonAsync<PluginProfileDetail>($"/plugin/profiles/{characterId}");

    public Task<List<PluginProfileSummary>?> GetPendingProfileApprovalsAsync(ulong guildId) =>
        GetJsonAsync<List<PluginProfileSummary>>($"/plugin/manage/profiles/pending?guild_id={guildId}");

    public Task<PluginProfileDetail?> GetProfileApprovalDetailAsync(ulong guildId, int characterId) =>
        GetJsonAsync<PluginProfileDetail>($"/plugin/manage/profiles/{characterId}?guild_id={guildId}");

    public Task<bool> ApproveProfileAsync(ulong guildId, int characterId) =>
        PostOkAsync($"/plugin/manage/profiles/{characterId}/approve?guild_id={guildId}");

    public Task<bool> RejectProfileAsync(ulong guildId, int characterId, string reason) =>
        PostOkAsync($"/plugin/manage/profiles/{characterId}/reject?guild_id={guildId}", new { reason });

    public Task<List<PluginVipTierSummary>?> GetVipTiersForManageAsync(ulong guildId) =>
        GetJsonAsync<List<PluginVipTierSummary>>($"/plugin/manage/vip/tiers?guild_id={guildId}");

    public Task<List<PluginVipMemberSummary>?> GetVipMembersForManageAsync(ulong guildId) =>
        GetJsonAsync<List<PluginVipMemberSummary>>($"/plugin/manage/vip/members?guild_id={guildId}");

    public Task<PluginVipMemberDetail?> GetVipMemberDetailAsync(ulong guildId, int memberId) =>
        GetJsonAsync<PluginVipMemberDetail>($"/plugin/manage/vip/members/{memberId}?guild_id={guildId}");

    public Task<PluginVipMemberDetail?> AssignVipMemberAsync(ulong guildId, ulong discordUserId, int tierId) =>
        PutJsonAsync<PluginVipMemberDetail>(
            $"/plugin/manage/vip/members?guild_id={guildId}",
            new { discord_user_id = discordUserId, tier_id = tierId }
        );

    public Task<bool> RemoveVipMemberAsync(ulong guildId, int memberId) =>
        DeleteOkAsync($"/plugin/manage/vip/members/{memberId}?guild_id={guildId}");

    public Task<PluginResolveCharacterResponse?> ResolveCharacterAsync(ulong guildId, string characterName, string world)
    {
        var query = $"guild_id={guildId}&character_name={Uri.EscapeDataString(characterName)}&world={Uri.EscapeDataString(world)}";
        return GetJsonAsync<PluginResolveCharacterResponse>($"/plugin/manage/resolve-character?{query}");
    }

    public Task<List<PluginManageGiveawaySummary>?> GetManageGiveawaysAsync(ulong guildId) =>
        GetJsonAsync<List<PluginManageGiveawaySummary>>($"/plugin/manage/giveaways?guild_id={guildId}");

    public Task<List<PluginManageGiveawaySummary>?> GetManageGiveawaysConcludedAsync(ulong guildId) =>
        GetJsonAsync<List<PluginManageGiveawaySummary>>($"/plugin/manage/giveaways/concluded?guild_id={guildId}");

    public Task<PluginManageGiveawaySummary?> RollGiveawayAsync(ulong guildId, int giveawayId, bool force = false) =>
        PostJsonAsync<PluginManageGiveawaySummary>(
            $"/plugin/manage/giveaways/{giveawayId}/roll?guild_id={guildId}&force={force}"
        );

    public Task<List<PluginManageRaffleSummary>?> GetManageRafflesAsync(ulong guildId) =>
        GetJsonAsync<List<PluginManageRaffleSummary>>($"/plugin/manage/raffles?guild_id={guildId}");

    public Task<List<PluginManageRaffleSummary>?> GetManageRafflesConcludedAsync(ulong guildId) =>
        GetJsonAsync<List<PluginManageRaffleSummary>>($"/plugin/manage/raffles/concluded?guild_id={guildId}");

    public Task<PluginManageRaffleSummary?> RollRaffleAsync(ulong guildId, int raffleId, bool force = false) =>
        PostJsonAsync<PluginManageRaffleSummary>(
            $"/plugin/manage/raffles/{raffleId}/roll?guild_id={guildId}&force={force}"
        );

    public Task<PluginManageRaffleSummary?> CreditRaffleTicketsAsync(ulong guildId, int raffleId, ulong discordUserId, int quantity) =>
        PutJsonAsync<PluginManageRaffleSummary>(
            $"/plugin/manage/raffles/{raffleId}/tickets?guild_id={guildId}",
            new { discord_user_id = discordUserId, quantity }
        );

    public async Task<bool> RevokeAsync()
    {
        try
        {
            return await DeleteOkAsync("/plugin-auth/me");
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
