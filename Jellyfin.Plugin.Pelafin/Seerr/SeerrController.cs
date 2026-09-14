using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.Json;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.Pelafin.Seerr;

[ApiController]
[Route("Pelafin/Seerr")]
[Authorize(Policy = "DefaultAuthorization")]
public sealed partial class SeerrController(ILibraryManager libraryManager) : ControllerBase
{
    // Reject redirects so a misconfigured upstream cannot forward the secret key.
    private static readonly HttpClient Http = new(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false })
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    [HttpGet("status")]
    public IActionResult GetStatus() => Ok(new { enabled = IsEnabled(Plugin.Instance?.Configuration), searchAvailable = true });

    [HttpGet("settings")]
    [Authorize(Policy = "RequiresElevation")]
    public IActionResult GetSettings()
    {
        var config = Plugin.Instance?.Configuration;
        return Ok(new
        {
            enabled = config?.SeerrEnabled ?? false,
            url = config?.SeerrUrl ?? "",
            hasApiKey = !string.IsNullOrWhiteSpace(config?.SeerrApiKey)
        });
    }

    [HttpPut("settings")]
    [Authorize(Policy = "RequiresElevation")]
    public IActionResult SaveSettings([FromBody] SeerrSettings settings)
    {
        var plugin = Plugin.Instance;
        if (plugin is null) return StatusCode(503);
        var url = settings.Url.Trim().TrimEnd('/');
        if ((settings.Enabled || url.Length > 0) && !SeerrClient.IsValidUrl(url))
            return BadRequest(new { code = "invalid_url" });
        var apiKey = settings.ApiKey?.Trim() ?? plugin.Configuration.SeerrApiKey;
        if (apiKey.Any(char.IsControl) || (settings.Enabled && string.IsNullOrWhiteSpace(apiKey)))
            return BadRequest(new { code = "api_key_required" });
        plugin.Configuration.SeerrEnabled = settings.Enabled;
        plugin.Configuration.SeerrUrl = url;
        plugin.Configuration.SeerrApiKey = apiKey;
        plugin.SaveConfiguration();
        return NoContent();
    }

    [HttpPost("items/{itemId:guid}/issues")]
    [RequestSizeLimit(16384)]
    public async Task<IActionResult> CreateIssue(Guid itemId, [FromBody] IssueReport report,
        CancellationToken cancellationToken)
    {
        // Jellyfin authenticates the token and sets this claim. Ignore all client
        // identity headers and reject service API keys without a signed-in user.
        if (!Guid.TryParse(User.FindFirst("Jellyfin-UserId")?.Value, out var userId) || userId == Guid.Empty
            || string.Equals(User.FindFirst("Jellyfin-IsApiKey")?.Value, "true", StringComparison.OrdinalIgnoreCase))
            return Unauthorized(new { code = "not_authenticated" });
        if (string.IsNullOrWhiteSpace(report.Message)) return BadRequest(new { code = "message_required" });
        var config = Plugin.Instance?.Configuration;
        if (!IsEnabled(config)) return StatusCode(503, new { code = "not_configured" });

        if (itemId == Guid.Empty) return NotFound(new { code = "item_not_found" });

        // This overload checks the user's access to the item, including library restrictions.
        var item = libraryManager.GetItemById<BaseItem>(itemId, userId);
        if (item is null) return NotFound(new { code = "item_not_found" });
        BaseItem? providerItem = item;
        int? season = null;
        int? episodeNumber = null;
        var mediaType = "movie";
        if (item is Episode episode)
        {
            if (episode.SeriesId == Guid.Empty) return BadRequest(new { code = "episode_metadata_missing" });
            providerItem = libraryManager.GetItemById<Series>(episode.SeriesId, userId);
            season = episode.ParentIndexNumber;
            episodeNumber = episode.IndexNumber;
            mediaType = "tv";
            if (season is null or < 0 || episodeNumber is null or <= 0)
                return BadRequest(new { code = "episode_metadata_missing" });
        }
        else if (item is not Movie)
        {
            return BadRequest(new { code = "unsupported_item" });
        }
        if (providerItem is null || !providerItem.ProviderIds.TryGetValue("Tmdb", out var tmdb)
            || !int.TryParse(tmdb, NumberStyles.None, CultureInfo.InvariantCulture, out var tmdbId) || tmdbId <= 0)
            return BadRequest(new { code = "tmdb_missing" });
        try
        {
            var client = new SeerrClient(Http, config!.SeerrUrl, config.SeerrApiKey);
            var id = await client.CreateIssueAsync(userId, new IssueMedia(mediaType, tmdbId, season, episodeNumber),
                report.IssueType, report.Message.Trim(), cancellationToken).ConfigureAwait(false);
            return StatusCode(StatusCodes.Status201Created, new { id });
        }
        catch (SeerrException e) { return StatusCode(e.StatusCode, new { code = e.Code }); }
        catch (HttpRequestException) { return StatusCode(502, new { code = "connection_failed" }); }
        catch (OperationCanceledException) { return StatusCode(504, new { code = "request_timeout" }); }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        { return StatusCode(502, new { code = "invalid_response" }); }
    }

    private static bool IsEnabled(PluginConfiguration? config) => config is not null
        && config.SeerrEnabled && SeerrClient.IsValidUrl(config.SeerrUrl)
        && !string.IsNullOrWhiteSpace(config.SeerrApiKey);
}

public sealed class IssueReport
{
    [Range(1, 4)] public int IssueType { get; set; }
    [Required, StringLength(4000, MinimumLength = 1)] public string Message { get; set; } = "";
}

public sealed class SeerrSettings
{
    public bool Enabled { get; set; }
    [Required(AllowEmptyStrings = true), StringLength(2048)] public string Url { get; set; } = "";
    [StringLength(512)] public string? ApiKey { get; set; }
}
