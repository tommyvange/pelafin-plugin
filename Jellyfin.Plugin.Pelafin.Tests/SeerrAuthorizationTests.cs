using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Jellyfin.Plugin.Pelafin.Seerr;
using MediaBrowser.Common.Api;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

// Exercise ASP.NET authorization and routing, not just direct controller calls.
// The test authentication handler substitutes for Jellyfin's token validation.
public sealed class SeerrAuthorizationTests : IAsyncLifetime
{
    private WebApplication _app = null!;
    private HttpClient _http = null!;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Testing",
            ApplicationName = typeof(SeerrController).Assembly.FullName
        });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Services.AddAuthentication("CustomAuthentication")
            .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>("CustomAuthentication", _ => { });
        builder.Services.AddAuthorization(options =>
        {
            // Match Jellyfin 10.11's policy registration: the default is unnamed.
            // Session validation is stubbed; the elevation policy matches Jellyfin.
            options.DefaultPolicy = new AuthorizationPolicyBuilder("CustomAuthentication")
                .RequireAuthenticatedUser().Build();
            options.AddPolicy(Policies.RequiresElevation, policy => policy
                .AddAuthenticationSchemes("CustomAuthentication")
                .RequireClaim(ClaimTypes.Role, "Administrator"));
        });
        builder.Services.AddControllers().AddApplicationPart(typeof(SeerrController).Assembly)
            .AddControllersAsServices();
        // Status/settings and rejected requests must not call the library manager.
        builder.Services.AddTransient(_ => new SeerrController(null!));
        _app = builder.Build();
        _app.UseAuthentication();
        _app.UseAuthorization();
        _app.MapControllers();
        await _app.StartAsync();
        _http = new HttpClient { BaseAddress = new Uri(_app.Urls.Single()) };
    }

    public async Task DisposeAsync()
    {
        _http.Dispose();
        await _app.DisposeAsync();
    }

    [Theory]
    [InlineData("GET", "status")]
    [InlineData("GET", "settings")]
    [InlineData("PUT", "settings")]
    [InlineData("GET", "search?query=test")]
    [InlineData("GET", "media/movie/123")]
    [InlineData("POST", "requests")]
    [InlineData("POST", "items/77c1d098-20db-4cce-a7fd-905f6971bf98/issues")]
    public async Task AnonymousRequestsAreRejectedByMiddleware(string method, string path)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), "Pelafin/Seerr/" + path);
        using var response = await _http.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task SignedInUserCanReadStatusWithoutANamedDefaultPolicy()
    {
        using var request = AuthenticatedRequest(HttpMethod.Get, "status", "member");
        using var response = await _http.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.GetProperty("searchAvailable").GetBoolean());
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("PUT")]
    public async Task OrdinaryUsersCannotReadOrChangeIntegrationSettings(string method)
    {
        using var request = AuthenticatedRequest(new HttpMethod(method), "settings", "member");
        request.Content = JsonContent.Create(new { enabled = false, url = "" });
        using var response = await _http.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AdministratorCanReadSettingsAndNeverReceivesTheApiKey()
    {
        using var request = AuthenticatedRequest(HttpMethod.Get, "settings", "admin");
        using var response = await _http.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(new[] { "enabled", "hasApiKey", "url" },
            json.EnumerateObject().Select(p => p.Name).Order().ToArray());
    }

    [Fact]
    public async Task ServiceKeyCannotSubmitAnIssueAsAUser()
    {
        using var request = AuthenticatedRequest(HttpMethod.Post,
            "items/77c1d098-20db-4cce-a7fd-905f6971bf98/issues", "service-key");
        request.Content = JsonContent.Create(new { issueType = 1, message = "Test report" });
        using var response = await _http.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static HttpRequestMessage AuthenticatedRequest(HttpMethod method, string path, string identity)
    {
        var request = new HttpRequestMessage(method, "Pelafin/Seerr/" + path);
        request.Headers.Authorization = new("Bearer", identity);
        return request;
    }

    public sealed class TestAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger, UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var identity = Request.Headers.Authorization.ToString();
            if (identity is not ("Bearer member" or "Bearer admin" or "Bearer service-key"))
                return Task.FromResult(AuthenticateResult.NoResult());
            var claims = new List<Claim>
            {
                new("Jellyfin-UserId", "77c1d098-20db-4cce-a7fd-905f6971bf98"),
                new("Jellyfin-IsApiKey", (identity == "Bearer service-key").ToString())
            };
            if (identity == "Bearer admin") claims.Add(new(ClaimTypes.Role, "Administrator"));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(
                new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name)), Scheme.Name)));
        }
    }
}
