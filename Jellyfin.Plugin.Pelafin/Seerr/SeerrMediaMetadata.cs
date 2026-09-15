using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.Pelafin.Seerr;

public sealed record SeerrCredit(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("profilePath")] string? ProfilePath);
public sealed record SeerrContentRating(
    [property: JsonPropertyName("country")] string Country,
    [property: JsonPropertyName("rating")] string Rating);
public sealed record SeerrTrailer(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("youtubeKey")] string YoutubeKey);
public sealed record SeerrMediaMetadata(
    [property: JsonPropertyName("originalTitle")] string? OriginalTitle,
    [property: JsonPropertyName("originalLanguage")] string? OriginalLanguage,
    [property: JsonPropertyName("productionCountries")] IReadOnlyList<string> ProductionCountries,
    [property: JsonPropertyName("genres")] IReadOnlyList<string> Genres,
    [property: JsonPropertyName("runtimeMinutes")] IReadOnlyList<int> RuntimeMinutes,
    [property: JsonPropertyName("status")] string? Status,
    [property: JsonPropertyName("ratings")] IReadOnlyList<SeerrContentRating> Ratings,
    [property: JsonPropertyName("cast")] IReadOnlyList<SeerrCredit> Cast,
    [property: JsonPropertyName("crew")] IReadOnlyList<SeerrCredit> Crew,
    [property: JsonPropertyName("trailers")] IReadOnlyList<SeerrTrailer> Trailers);

public sealed partial class SeerrClient
{
    // Project only public catalog fields from the already-authenticated details response.
    // Never forward raw mediaInfo, requesters, service configuration, or arbitrary URLs.
    private static SeerrMediaMetadata MapMetadata(JsonElement data, string type)
    {
        var countries = Array(data, "productionCountries").Select(c => Text(c, "name"))
            .OfType<string>().Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().ToArray();
        var genres = Array(data, "genres").Select(g => Text(g, "name"))
            .OfType<string>().Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().ToArray();
        var runtimes = type == "movie" ? new[] { Number(data, "runtime") }
            : Array(data, "episodeRunTime").Where(v => v.ValueKind == JsonValueKind.Number)
                .Select(v => v.TryGetInt32(out var n) ? n : 0).ToArray();
        var ratings = Array(Object(data, type == "movie" ? "releases" : "contentRatings"), "results")
            .Select(r => new SeerrContentRating(Text(r, "iso_3166_1") ?? "",
                NonEmpty(Text(r, "rating")) ?? Array(r, "release_dates")
                    // Prefer the theatrical certification when release formats differ.
                    .OrderBy(d => Number(d, "type") == 3 ? 0 : 1)
                    .Select(d => NonEmpty(Text(d, "certification"))).OfType<string>().FirstOrDefault() ?? ""))
            .Where(r => r.Rating.Length > 0 && Regex.IsMatch(r.Country, "^[A-Z]{2}$"))
            .DistinctBy(r => r.Country).ToArray();
        var credits = Object(data, "credits");
        var cast = Array(credits, "cast").OrderBy(c => Number(c, "order"))
            .Select(c => Credit(c, Text(c, "character") ?? ""))
            .Where(c => c.Id > 0 && c.Name.Length > 0).DistinctBy(c => c.Id).Take(100).ToArray();
        var crew = Array(data, "createdBy").Select(c => Credit(c, "Creator"))
            .Concat(Array(credits, "crew").Select(c => Credit(c, Text(c, "job") ?? Text(c, "department") ?? "")))
            .Where(c => c.Id > 0 && c.Name.Length > 0).DistinctBy(c => (c.Id, c.Role)).Take(200).ToArray();
        var trailers = Array(data, "relatedVideos")
            .Where(v => Text(v, "site") == "YouTube" && Text(v, "type") is "Trailer" or "Teaser")
            .OrderBy(v => Text(v, "type") == "Trailer" ? 0 : 1)
            .Where(v => Regex.IsMatch(Text(v, "key") ?? "", "^[a-zA-Z0-9_-]{11}$"))
            .Select(v => new SeerrTrailer(Text(v, "name") ?? "Trailer", Text(v, "key")!))
            .DistinctBy(v => v.YoutubeKey).Take(10).ToArray();
        return new SeerrMediaMetadata(
            NonEmpty(Text(data, type == "movie" ? "originalTitle" : "originalName")),
            NonEmpty(Text(data, "originalLanguage")), countries, genres,
            runtimes.Where(n => n > 0).Distinct().Order().ToArray(),
            type == "tv" ? NonEmpty(Text(data, "status")) : null, ratings, cast, crew, trailers);
    }

    private static SeerrCredit Credit(JsonElement credit, string role) =>
        new(Number(credit, "id"), Text(credit, "name")?.Trim() ?? "", role,
            SafeImagePath(Text(credit, "profilePath") ?? Text(credit, "profile_path")));
    private static string? NonEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string? SafeImagePath(string? value) => value is not null
        && Regex.IsMatch(value, @"^/[a-zA-Z0-9_-]+\.(jpg|png|webp)$", RegexOptions.CultureInvariant) ? value : null;
}
