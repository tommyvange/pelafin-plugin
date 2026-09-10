using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.Pelafin;

[ApiController]
[Route("Pelafin/Logo")]
public class LogoController : ControllerBase
{
    [HttpGet("{mode}")]
    [AllowAnonymous]
    public IActionResult GetLogo(string mode)
    {
        if (!TryGetLogoInfo(mode, out var path, out var contentType) || contentType is null || !System.IO.File.Exists(path))
        {
            return NotFound();
        }

        return PhysicalFile(path, contentType);
    }

    [HttpPost("{mode}")]
    [Authorize(Policy = "RequiresElevation")]
    public async Task<IActionResult> UploadLogo(string mode)
    {
        if (!TryGetLogoInfo(mode, out var path, out _))
        {
            return NotFound();
        }

        var contentType = Request.ContentType;
        if (string.IsNullOrEmpty(contentType) || !contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest("Request body must be an image.");
        }

        await using (var fileStream = System.IO.File.Create(path))
        {
            await Request.Body.CopyToAsync(fileStream).ConfigureAwait(false);
        }

        SetContentType(mode, contentType);
        Plugin.Instance!.SaveConfiguration();

        return NoContent();
    }

    private static bool TryGetLogoInfo(string mode, out string path, out string? contentType)
    {
        path = string.Empty;
        contentType = null;

        if (Plugin.Instance is null)
        {
            return false;
        }

        var dataFolder = Plugin.Instance.DataFolderPath;
        Directory.CreateDirectory(dataFolder);

        if (string.Equals(mode, "light", StringComparison.OrdinalIgnoreCase))
        {
            path = Path.Combine(dataFolder, "logo-light");
            contentType = Plugin.Instance.Configuration.LogoLightContentType;
            return true;
        }

        if (string.Equals(mode, "dark", StringComparison.OrdinalIgnoreCase))
        {
            path = Path.Combine(dataFolder, "logo-dark");
            contentType = Plugin.Instance.Configuration.LogoDarkContentType;
            return true;
        }

        return false;
    }

    private static void SetContentType(string mode, string contentType)
    {
        if (string.Equals(mode, "light", StringComparison.OrdinalIgnoreCase))
        {
            Plugin.Instance!.Configuration.LogoLightContentType = contentType;
        }
        else
        {
            Plugin.Instance!.Configuration.LogoDarkContentType = contentType;
        }
    }
}
