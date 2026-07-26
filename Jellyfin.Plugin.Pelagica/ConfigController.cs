using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.Pelagica;

[ApiController]
[Route("Pelagica/Config")]
public class ConfigController : ControllerBase
{
    [HttpGet]
    [AllowAnonymous]
    public ContentResult GetConfig()
    {
        var json = Plugin.Instance?.Configuration.AppConfigJson ?? "{}";
        return Content(json, "application/json");
    }
    
    [HttpPost]
    [Authorize(Policy = "RequiresElevation")]
    public async Task<IActionResult> SaveConfig()
    {
        using var reader = new StreamReader(Request.Body);
        var body = await reader.ReadToEndAsync().ConfigureAwait(false);

        try
        {
            using var _ = JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
            return BadRequest("Request body is not valid JSON.");
        }

        if (Plugin.Instance is null)
        {
            return StatusCode(StatusCodes.Status500InternalServerError);
        }

        Plugin.Instance.Configuration.AppConfigJson = body;
        Plugin.Instance.SaveConfiguration();

        return NoContent();
    }
}
