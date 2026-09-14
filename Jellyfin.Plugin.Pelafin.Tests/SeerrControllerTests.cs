using System.Security.Claims;
using Jellyfin.Plugin.Pelafin.Seerr;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Xunit;

public class SeerrControllerTests
{
    [Theory]
    [InlineData(null, false)]
    [InlineData("invalid-guid", false)]
    [InlineData("00000000-0000-0000-0000-000000000000", false)]
    [InlineData("77c1d098-20db-4cce-a7fd-905f6971bf98", true)]
    public async Task RejectsMissingUserIdentityAndServiceKeys(string? id, bool serviceKey)
    {
        var claims = new List<Claim>();
        if (id is not null) claims.Add(new Claim("Jellyfin-UserId", id));
        claims.Add(new Claim("Jellyfin-IsApiKey", serviceKey.ToString()));
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"))
        };
        // A caller-supplied identity header never authenticates a user.
        context.Request.Headers["X-Api-User"] = "1";
        context.Request.Headers["X-Jellyfin-UserId"] = "77c1d098-20db-4cce-a7fd-905f6971bf98";
        var controller = new SeerrController(null!)
        {
            ControllerContext = new ControllerContext { HttpContext = context }
        };
        var result = await controller.CreateIssue(Guid.NewGuid(), new IssueReport
        {
            IssueType = 1, Message = "Broken video"
        }, default);
        Assert.IsType<UnauthorizedObjectResult>(result);
        Assert.IsType<UnauthorizedObjectResult>(await controller.Search("movie"));
        Assert.IsType<UnauthorizedObjectResult>(await controller.RequestDetails("movie", 123, default));
        Assert.IsType<UnauthorizedObjectResult>(await controller.CreateRequest(new MediaRequest { MediaType = "movie", TmdbId = 123 }, default));
    }
}
