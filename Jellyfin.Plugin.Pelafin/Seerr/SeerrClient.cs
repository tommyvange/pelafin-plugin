using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Pelafin.Seerr;

public sealed class SeerrException(string code, int statusCode = 502) : Exception(code)
{
    public string Code { get; } = code;
    public int StatusCode { get; } = statusCode;
}

public sealed record IssueMedia(string MediaType, int TmdbId, int? Season = null, int? Episode = null);

public sealed partial class SeerrClient(HttpClient http, string serverUrl, string apiKey)
{
    private readonly Uri _baseUri = new(serverUrl.TrimEnd('/') + "/api/v1/");
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static bool IsValidUrl(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && (uri.Scheme == "http" || uri.Scheme == "https")
        && string.IsNullOrEmpty(uri.UserInfo)
        && string.IsNullOrEmpty(uri.Query)
        && string.IsNullOrEmpty(uri.Fragment);

    public async Task<int> CreateIssueAsync(Guid jellyfinUserId, IssueMedia media, int issueType,
        string message, CancellationToken cancellationToken)
    {
        // Resolve only by the authenticated Jellyfin GUID, never by a client-supplied
        // Seerr user ID, display name, or email. Never fall back to the API key owner.
        var userId = await ResolveUserAsync(jellyfinUserId, cancellationToken).ConfigureAwait(false);
        using var details = await SendAsync(HttpMethod.Get, $"{media.MediaType}/{media.TmdbId}",
            userId, null, cancellationToken).ConfigureAwait(false);
        if (!details.RootElement.TryGetProperty("mediaInfo", out var info)
            || info.ValueKind != JsonValueKind.Object
            || !info.TryGetProperty("id", out var id) || !id.TryGetInt32(out var mediaId) || mediaId <= 0)
        {
            throw new SeerrException("media_not_synced", 409);
        }

        // mediaId is Seerr's internal media ID, not the TMDB ID. Episode issues use
        // the series' media record with season/episode numbers (including season 0).
        using var result = await SendAsync(HttpMethod.Post, "issue", userId, new
        {
            mediaId,
            issueType,
            message,
            problemSeason = media.Season,
            problemEpisode = media.Episode
        }, cancellationToken).ConfigureAwait(false);
        if (!result.RootElement.TryGetProperty("id", out var issueId)
            || !issueId.TryGetInt32(out var value) || value <= 0)
        {
            throw new SeerrException("invalid_response");
        }
        return value;
    }

    private async Task<int> ResolveUserAsync(Guid jellyfinUserId, CancellationToken cancellationToken, long requiredPermissions = 2 | 1048576 | 4194304)
    {
        // Paginated lookup works with Seerr releases predating /user/jellyfin/:id.
        const int pageSize = 100;
        for (var skip = 0; ; skip += pageSize)
        {
            using var page = await SendAsync(HttpMethod.Get, $"user?take={pageSize}&skip={skip}",
                null, null, cancellationToken).ConfigureAwait(false);
            var users = page.RootElement.GetProperty("results");
            foreach (var user in users.EnumerateArray())
            {
                if (!user.TryGetProperty("jellyfinUserId", out var guid)
                    || guid.ValueKind != JsonValueKind.String
                    || !Guid.TryParse(guid.GetString(), out var parsed) || parsed != jellyfinUserId)
                {
                    continue;
                }
                var userId = user.GetProperty("id").GetInt32();
                if (userId <= 0) throw new SeerrException("invalid_response");
                var permissions = user.GetProperty("permissions").GetInt64();
                if (requiredPermissions != 0 && (permissions & requiredPermissions) == 0)
                    throw new SeerrException("permission_denied", 403);
                return userId;
            }
            var total = page.RootElement.GetProperty("pageInfo").GetProperty("results").GetInt32();
            if (users.GetArrayLength() == 0 || skip + pageSize >= total) break;
        }
        throw new SeerrException("user_not_synced", 403);
    }

    private async Task<JsonDocument> SendAsync(HttpMethod method, string path, int? userId,
        object? body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, new Uri(_baseUri, path));
        request.Headers.Add("X-Api-Key", apiKey);
        if (userId.HasValue)
            request.Headers.Add("X-Api-User", userId.Value.ToString(CultureInfo.InvariantCulture));
        if (body is not null) request.Content = JsonContent.Create(body, options: JsonOptions);
        using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            // Don't return upstream bodies: they can contain server configuration.
            throw response.StatusCode switch
            {
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                    new SeerrException(userId.HasValue ? "permission_denied" : "connection_failed",
                        userId.HasValue ? 403 : 502),
                HttpStatusCode.Conflict => new SeerrException("already_requested", 409),
                HttpStatusCode.NotFound => new SeerrException("media_not_synced", 409),
                HttpStatusCode.TooManyRequests => new SeerrException("rate_limited", 429),
                _ => new SeerrException("connection_failed")
            };
        }
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
    }
}
