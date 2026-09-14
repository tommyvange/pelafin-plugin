using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.Pelafin.Seerr;

public sealed partial class SeerrController
{
    [HttpGet("search")]
    public Task<IActionResult> Search([FromQuery, Required, StringLength(200, MinimumLength = 1)] string query,
        [FromQuery, Range(1, 500)] int page = 1, CancellationToken cancellationToken = default) =>
        WithSearchUser(async (client, userId) =>
        {
            if (string.IsNullOrWhiteSpace(query)) return BadRequest(new { code = "query_required" });
            return Ok(await client.SearchAsync(userId, query.Trim(), page, cancellationToken).ConfigureAwait(false));
        });

    [HttpGet("media/{mediaType}/{tmdbId:int}")]
    public Task<IActionResult> RequestDetails(string mediaType, int tmdbId, CancellationToken cancellationToken) =>
        WithSearchUser(async (client, userId) => Ok(await client.GetRequestDetailsAsync(userId, mediaType, tmdbId, cancellationToken).ConfigureAwait(false)));

    [HttpPost("requests")]
    [RequestSizeLimit(4096)]
    public Task<IActionResult> CreateRequest([FromBody] MediaRequest request, CancellationToken cancellationToken) =>
        WithSearchUser(async (client, userId) => StatusCode(201,
            await client.RequestAsync(userId, request.MediaType, request.TmdbId, request.Seasons, cancellationToken).ConfigureAwait(false)));

    private async Task<IActionResult> WithSearchUser(Func<SeerrClient, Guid, Task<IActionResult>> action)
    {
        if (!Guid.TryParse(User.FindFirst("Jellyfin-UserId")?.Value, out var userId) || userId == Guid.Empty
            || string.Equals(User.FindFirst("Jellyfin-IsApiKey")?.Value, "true", StringComparison.OrdinalIgnoreCase))
            return Unauthorized(new { code = "not_authenticated" });
        var config = Plugin.Instance?.Configuration;
        if (!IsEnabled(config) || !SearchEnabled(config!)) return StatusCode(503, new { code = "not_configured" });
        try { return await action(new SeerrClient(Http, config!.SeerrUrl, config.SeerrApiKey), userId).ConfigureAwait(false); }
        catch (SeerrException e) { return StatusCode(e.StatusCode, new { code = e.Code }); }
        catch (HttpRequestException) { return StatusCode(502, new { code = "connection_failed" }); }
        catch (OperationCanceledException) { return StatusCode(504, new { code = "request_timeout" }); }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        { return StatusCode(502, new { code = "invalid_response" }); }
    }

    private static bool SearchEnabled(PluginConfiguration config)
    {
        try
        {
            using var data = JsonDocument.Parse(config.AppConfigJson);
            return data.RootElement.TryGetProperty("search", out var search)
                && search.ValueKind == JsonValueKind.Object && search.TryGetProperty("seerr", out var seerr)
                && seerr.ValueKind == JsonValueKind.Object && seerr.TryGetProperty("enabled", out var enabled)
                && enabled.ValueKind == JsonValueKind.True;
        }
        catch (JsonException) { return false; }
    }
}

public sealed class MediaRequest
{
    [Required, RegularExpression("^(movie|tv)$")] public string MediaType { get; set; } = "";
    [Range(1, int.MaxValue)] public int TmdbId { get; set; }
    [MaxLength(100)] public int[]? Seasons { get; set; }
}
