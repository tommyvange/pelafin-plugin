using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.Pelagica;

[ApiController]
[Route("Pelagica")]
public class StatusController : ControllerBase
{
    [HttpGet("Enabled")]
    [AllowAnonymous]
    public ActionResult<bool> GetEnabled()
    {
        return Plugin.Instance is not null;
    }
}
