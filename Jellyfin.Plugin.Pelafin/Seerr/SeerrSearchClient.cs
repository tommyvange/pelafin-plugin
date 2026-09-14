using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.Pelafin.Seerr;

// Jellyfin uses PascalCase for its own DTOs. Pin the bridge's JSON contract
// explicitly so its responses match the app regardless of host serializer defaults.
public sealed record SeerrSearchItem(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("mediaType")] string MediaType,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("date")] string? Date,
    [property: JsonPropertyName("posterPath")] string? PosterPath,
    [property: JsonPropertyName("overview")] string Overview,
    [property: JsonPropertyName("requested")] bool Requested);
public sealed record SeerrSearchPage(
    [property: JsonPropertyName("page")] int Page,
    [property: JsonPropertyName("totalPages")] int TotalPages,
    [property: JsonPropertyName("results")] IReadOnlyList<SeerrSearchItem> Results);
public sealed record SeerrSeason(
    [property: JsonPropertyName("seasonNumber")] int SeasonNumber,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("episodeCount")] int EpisodeCount,
    [property: JsonPropertyName("available")] bool Available,
    [property: JsonPropertyName("requested")] bool Requested);
public sealed record SeerrRequestDetails(
    [property: JsonPropertyName("media")] SeerrSearchItem Media,
    [property: JsonPropertyName("seasons")] IReadOnlyList<SeerrSeason> Seasons);
public sealed record SeerrRequestResult(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("status")] int Status);

public sealed partial class SeerrClient
{
    public async Task<SeerrSearchPage> SearchAsync(Guid jellyfinUserId, string query, int page, CancellationToken ct)
    {
        var userId = await ResolveUserAsync(jellyfinUserId, ct, 0).ConfigureAwait(false);
        using var data = await SendAsync(HttpMethod.Get, $"search?query={Uri.EscapeDataString(query)}&page={page}", userId, null, ct).ConfigureAwait(false);
        var root = data.RootElement;
        var results = new List<SeerrSearchItem>();
        foreach (var item in root.GetProperty("results").EnumerateArray())
        {
            var type = Text(item, "mediaType");
            if (type is not ("movie" or "tv") || ExcludedFromDiscovery(item)) continue;
            results.Add(MapMedia(item, type));
        }
        return new SeerrSearchPage(page, Math.Clamp(Number(root, "totalPages"), 0, 500), results);
    }

    public async Task<SeerrRequestDetails> GetRequestDetailsAsync(Guid jellyfinUserId, string mediaType, int tmdbId, CancellationToken ct)
    {
        ValidateMedia(mediaType, tmdbId);
        var userId = await ResolveUserAsync(jellyfinUserId, ct, RequestPermissions(mediaType)).ConfigureAwait(false);
        return await GetRequestDetailsForUserAsync(userId, mediaType, tmdbId, ct).ConfigureAwait(false);
    }

    public async Task<SeerrRequestResult> RequestAsync(Guid jellyfinUserId, string mediaType, int tmdbId, int[]? seasons, CancellationToken ct)
    {
        ValidateMedia(mediaType, tmdbId);
        if (mediaType == "movie" && seasons is { Length: > 0 }) throw new SeerrException("invalid_seasons", 400);
        var userId = await ResolveUserAsync(jellyfinUserId, ct, RequestPermissions(mediaType)).ConfigureAwait(false);
        // Recheck availability and season selection immediately before writing. The upstream
        // remains authoritative for quotas, concurrent requests, and automatic approval.
        var details = await GetRequestDetailsForUserAsync(userId, mediaType, tmdbId, ct).ConfigureAwait(false);
        if (mediaType == "movie" && details.Media.Requested) throw new SeerrException("already_requested", 409);
        if (mediaType == "tv" && (seasons is not { Length: > 0 and <= 100 }
            || seasons.Distinct().Count() != seasons.Length
            || seasons.Any(number => !details.Seasons.Any(s => s.SeasonNumber == number && !s.Available && !s.Requested))))
            throw new SeerrException("invalid_seasons", 409);
        using var response = await SendAsync(HttpMethod.Post, "request", userId, new
        {
            mediaType, mediaId = tmdbId, seasons = mediaType == "tv" ? seasons : null, is4k = false
            // Never accept userId, ignoreQuota, server/profile/root folder overrides from the browser.
        }, ct).ConfigureAwait(false);
        var id = Number(response.RootElement, "id");
        var status = Number(response.RootElement, "status");
        // Seerr can return HTTP 202 with an error when no seasons remain. It is not a created request.
        if (id <= 0 || status is < 1 or > 5) throw new SeerrException("request_not_created", 409);
        return new SeerrRequestResult(id, status);
    }

    private async Task<SeerrRequestDetails> GetRequestDetailsForUserAsync(int userId, string mediaType, int tmdbId, CancellationToken ct)
    {
        using var response = await SendAsync(HttpMethod.Get, $"{mediaType}/{tmdbId}", userId, null, ct).ConfigureAwait(false);
        var data = response.RootElement;
        if (ExcludedFromDiscovery(data)) throw new SeerrException("already_available", 409);
        var media = MapMedia(data, mediaType);
        if (media.Id != tmdbId) throw new SeerrException("invalid_response");
        var info = Object(data, "mediaInfo");
        var existingSeasons = Array(info, "seasons");
        var requests = Array(info, "requests").Where(r => Number(r, "status") is 1 or 2 && !Flag(r, "is4k"));
        var requestedSeasons = requests.SelectMany(r => Array(r, "seasons"))
            .Where(s => Number(s, "status") is 1 or 2).Select(s => Number(s, "seasonNumber")).ToHashSet();
        var seasons = Array(data, "seasons").Where(s => Number(s, "episodeCount") > 0).Select(s =>
        {
            var number = Number(s, "seasonNumber");
            var known = existingSeasons.FirstOrDefault(e => Number(e, "seasonNumber") == number);
            return new SeerrSeason(number, Text(s, "name") ?? $"Season {number}", Number(s, "episodeCount"),
                Number(known, "status") is 4 or 5 || Number(known, "status4k") is 4 or 5,
                Number(known, "status") is 2 or 3 || requestedSeasons.Contains(number));
        }).ToArray();
        return new SeerrRequestDetails(media, seasons);
    }

    // Exclude available, partially available and blocklisted titles in either quality.
    // Seerr's Jellyfin scan is the source of availability, including titles outside the current query.
    private static bool ExcludedFromDiscovery(JsonElement item)
    {
        var info = Object(item, "mediaInfo");
        return Number(info, "status") is 4 or 5 or 6 || Number(info, "status4k") is 4 or 5 or 6;
    }
    private static SeerrSearchItem MapMedia(JsonElement item, string type)
    {
        var id = Number(item, "id");
        var title = Text(item, type == "movie" ? "title" : "name");
        if (id <= 0 || string.IsNullOrWhiteSpace(title)) throw new SeerrException("invalid_response");
        var poster = Text(item, "posterPath");
        if (poster is not null && !Regex.IsMatch(poster, @"^/[a-zA-Z0-9_-]+\.(jpg|png|webp)$", RegexOptions.CultureInvariant)) poster = null;
        var info = Object(item, "mediaInfo");
        return new(id, type, title, Text(item, type == "movie" ? "releaseDate" : "firstAirDate"), poster,
            Text(item, "overview") ?? "", Number(info, "status") is 2 or 3);
    }
    private static long RequestPermissions(string type) => 2 | 32 | (type == "movie" ? 262144 : 524288);
    private static void ValidateMedia(string type, int id)
    {
        if (type is not ("movie" or "tv") || id <= 0) throw new SeerrException("unsupported_request", 400);
    }
    private static JsonElement Object(JsonElement value, string key) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(key, out var child) ? child : default;
    private static int Number(JsonElement value, string key) => Object(value, key) is var child && child.ValueKind == JsonValueKind.Number && child.TryGetInt32(out var n) ? n : 0;
    private static string? Text(JsonElement value, string key) => Object(value, key) is var child && child.ValueKind == JsonValueKind.String ? child.GetString() : null;
    private static bool Flag(JsonElement value, string key) => Object(value, key).ValueKind == JsonValueKind.True;
    private static IEnumerable<JsonElement> Array(JsonElement value, string key) => Object(value, key) is var child && child.ValueKind == JsonValueKind.Array ? child.EnumerateArray() : [];
}
