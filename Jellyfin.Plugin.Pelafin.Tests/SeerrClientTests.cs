using System.Net;
using System.Text;
using System.Text.Json;
using Jellyfin.Plugin.Pelafin.Seerr;
using Xunit;

public class SeerrClientTests
{
    private static readonly Guid UserId = Guid.Parse("77c1d098-20db-4cce-a7fd-905f6971bf98");
    private const string Key = "server-only-secret";

    [Theory]
    [InlineData("movie", null, null)]
    [InlineData("tv", 2, 4)]
    [InlineData("tv", 0, 1)]
    public async Task ReportsAsSyncedUserWithInternalMediaIdAndEpisodeContext(string type, int? season, int? episode)
    {
        var handler = new StubHandler(async (request, count) =>
        {
            Assert.Equal(Key, request.Headers.GetValues("X-Api-Key").Single());
            Assert.False(request.Headers.Contains("Authorization"));
            if (count == 1)
            {
                Assert.False(request.Headers.Contains("X-Api-User"));
                Assert.Equal("/seerr/api/v1/user?take=100&skip=0", request.RequestUri!.PathAndQuery);
                return Json($$$"""{"results":[{"id":17,"jellyfinUserId":"{{{UserId:N}}}","permissions":4194304}],"pageInfo":{"results":1}}""");
            }
            Assert.Equal("17", request.Headers.GetValues("X-Api-User").Single());
            if (count == 2)
            {
                Assert.Equal($"/seerr/api/v1/{type}/1234", request.RequestUri!.AbsolutePath);
                return Json("""{"id":1234,"mediaInfo":{"id":82}}""");
            }
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/seerr/api/v1/issue", request.RequestUri!.AbsolutePath);
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            var data = body.RootElement;
            Assert.Equal(82, data.GetProperty("mediaId").GetInt32());
            Assert.Equal(3, data.GetProperty("issueType").GetInt32());
            Assert.Equal("Subtitles out of sync at 12:30", data.GetProperty("message").GetString());
            Assert.False(data.TryGetProperty("userId", out _));
            if (season.HasValue)
            {
                Assert.Equal(season.Value, data.GetProperty("problemSeason").GetInt32());
                Assert.Equal(episode!.Value, data.GetProperty("problemEpisode").GetInt32());
            }
            else
            {
                Assert.False(data.TryGetProperty("problemSeason", out _));
                Assert.False(data.TryGetProperty("problemEpisode", out _));
            }
            return Json("""{"id":91}""", HttpStatusCode.Created);
        });
        var client = new SeerrClient(new HttpClient(handler), "https://example.com/seerr/", Key);
        Assert.Equal(91, await client.CreateIssueAsync(UserId, new IssueMedia(type, 1234, season, episode),
            3, "Subtitles out of sync at 12:30", default));
        Assert.Equal(3, handler.Count);
    }

    [Fact]
    public async Task FindsUserAfterFirstPageAndAcceptsHyphenatedGuid()
    {
        var handler = new StubHandler((request, count) => Task.FromResult(count switch
        {
            1 => Json("""{"results":[{"id":1,"jellyfinUserId":"00000000000000000000000000000001"}],"pageInfo":{"results":101}}"""),
            2 => UserPage(2),
            3 => Json("""{"mediaInfo":{"id":82}}"""),
            _ => Json("""{"id":91}""")
        }));
        await new SeerrClient(new HttpClient(handler), "https://example.com", Key)
            .CreateIssueAsync(UserId, new IssueMedia("movie", 1234), 1, "Broken video", default);
        Assert.Equal(4, handler.Count);
        Assert.Contains("skip=100", handler.Paths[1]);
    }

    [Theory]
    [InlineData(false, 4194304, "user_not_synced")]
    [InlineData(true, 32, "permission_denied")]
    public async Task MissingOrUnprivilegedUserNeverFallsBackToAdmin(bool synced, long permissions, string code)
    {
        var handler = new StubHandler((_, _) => Task.FromResult(synced ? UserPage(permissions) :
            Json("""{"results":[],"pageInfo":{"results":0}}""")));
        var error = await Assert.ThrowsAsync<SeerrException>(() =>
            new SeerrClient(new HttpClient(handler), "https://example.com", Key)
                .CreateIssueAsync(UserId, new IssueMedia("movie", 1234), 1, "Broken", default));
        Assert.Equal(code, error.Code);
        Assert.Equal(1, handler.Count);
    }

    [Fact]
    public async Task UnsyncedMediaNeverCreatesIssue()
    {
        var handler = new StubHandler((_, count) => Task.FromResult(count == 1 ? UserPage(4194304) : Json("""{"id":1234}""")));
        var error = await Assert.ThrowsAsync<SeerrException>(() =>
            new SeerrClient(new HttpClient(handler), "https://example.com", Key)
                .CreateIssueAsync(UserId, new IssueMedia("movie", 1234), 1, "Broken", default));
        Assert.Equal("media_not_synced", error.Code);
        Assert.Equal(2, handler.Count);
    }

    [Fact]
    public async Task FailedSubmissionIsNotRetriedAndUpstreamBodyIsNotExposed()
    {
        var handler = new StubHandler((_, count) => Task.FromResult(count switch
        {
            1 => UserPage(4194304),
            2 => Json("""{"mediaInfo":{"id":82}}"""),
            _ => Json(Key, HttpStatusCode.InternalServerError)
        }));
        var error = await Assert.ThrowsAsync<SeerrException>(() =>
            new SeerrClient(new HttpClient(handler), "https://example.com", Key)
                .CreateIssueAsync(UserId, new IssueMedia("movie", 1234), 1, "Broken", default));
        Assert.Equal("connection_failed", error.Code);
        Assert.DoesNotContain(Key, error.ToString());
        Assert.Equal(3, handler.Count);
    }

    [Theory]
    [InlineData("https://example.com/seerr", true)]
    [InlineData("http://localhost:5055", true)]
    [InlineData("file:///etc/passwd", false)]
    [InlineData("https://user:pass@example.com", false)]
    [InlineData("https://example.com?key=secret", false)]
    [InlineData("https://example.com/#x", false)]
    public void SettingsUrlValidation(string url, bool valid) => Assert.Equal(valid, SeerrClient.IsValidUrl(url));

    private static HttpResponseMessage UserPage(long permissions) => Json($$$"""{"results":[{"id":17,"jellyfinUserId":"{{{UserId}}}","permissions":{{{permissions}}}}],"pageInfo":{"results":1}}""");
    private static HttpResponseMessage Json(string text, HttpStatusCode code = HttpStatusCode.OK) => new(code)
    { Content = new StringContent(text, Encoding.UTF8, "application/json") };

    private sealed class StubHandler(Func<HttpRequestMessage, int, Task<HttpResponseMessage>> callback) : HttpMessageHandler
    {
        public int Count { get; private set; }
        public List<string> Paths { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Paths.Add(request.RequestUri!.PathAndQuery);
            return callback(request, ++Count);
        }
    }
}
