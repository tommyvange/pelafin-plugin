using System.Net;
using System.Text;
using System.Text.Json;
using Jellyfin.Plugin.Pelafin.Seerr;
using Xunit;

public class SeerrSearchTests
{
    private static readonly Guid UserId = Guid.Parse("77c1d098-20db-4cce-a7fd-905f6971bf98");
    private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) => new(status)
        { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    private static HttpResponseMessage User(long permissions = 32) => Json($$$"""{"results":[{"id":17,"jellyfinUserId":"{{{UserId:N}}}","permissions":{{{permissions}}}}],"pageInfo":{"results":1}}""");
    private static SeerrClient Client(Handler handler) => new(new HttpClient(handler), "https://example.com/seerr", "private-key");

    [Fact]
    public async Task SearchUsesSyncedUserWithoutIssuePermissionAndReturnsOnlyUnavailableMoviesAndShows()
    {
        var handler = new Handler((request, call) =>
        {
            if (call == 1) return Task.FromResult(User(0));
            Assert.Equal("17", request.Headers.GetValues("X-Api-User").Single());
            Assert.Equal("/seerr/api/v1/search?query=star%20%26%20moon&page=2", request.RequestUri!.PathAndQuery);
            return Task.FromResult(Json("""
            {"totalPages":4,"results":[
                {"id":1,"mediaType":"movie","title":"Missing","posterPath":"/poster.jpg","overview":"Story","mediaInfo":{"status":1,"secret":"private-key"}},
                {"id":2,"mediaType":"tv","name":"Pending","posterPath":"https://bad.example/key","mediaInfo":{"status":2}},
                {"id":3,"mediaType":"person","name":"Person"},
                {"id":4,"mediaType":"movie","title":"Available","mediaInfo":{"status":5}},
                {"id":5,"mediaType":"tv","name":"Partial","mediaInfo":{"status":4}},
                {"id":6,"mediaType":"movie","title":"4K","mediaInfo":{"status":1,"status4k":5}},
                {"id":7,"mediaType":"movie","title":"Blocked","mediaInfo":{"status":6}}
            ]}
            """));
        });
        var result = await Client(handler).SearchAsync(UserId, "star & moon", 2, default);
        Assert.Equal(2, result.Page);
        Assert.Equal(4, result.TotalPages);
        Assert.Equal(new[] { 1, 2 }, result.Results.Select(i => i.Id));
        Assert.False(result.Results[0].Requested);
        Assert.True(result.Results[1].Requested);
        Assert.Null(result.Results[1].PosterPath);
        Assert.DoesNotContain("private-key", JsonSerializer.Serialize(result));
    }

    [Theory]
    [InlineData("movie", 262144)]
    [InlineData("tv", 524288)]
    [InlineData("movie", 2)]
    [InlineData("tv", 32)]
    public async Task RequestsAreAttributedToUserWithOnlyWhitelistedFields(string type, long permissions)
    {
        var handler = new Handler(async (request, call) =>
        {
            if (call == 1) return User(permissions);
            Assert.Equal("17", request.Headers.GetValues("X-Api-User").Single());
            Assert.Equal("private-key", request.Headers.GetValues("X-Api-Key").Single());
            Assert.False(request.Headers.Contains("Authorization"));
            if (call == 2) return Details(type);
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.EndsWith("/request", request.RequestUri!.AbsolutePath);
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            var body = json.RootElement;
            Assert.Equal(123, body.GetProperty("mediaId").GetInt32());
            Assert.Equal(type, body.GetProperty("mediaType").GetString());
            Assert.False(body.GetProperty("is4k").GetBoolean());
            foreach (var field in new[] { "userId", "ignoreQuota", "serverId", "profileId", "rootFolder", "tags" })
                Assert.False(body.TryGetProperty(field, out _));
            if (type == "tv") Assert.Equal(2, body.GetProperty("seasons")[0].GetInt32());
            else Assert.False(body.TryGetProperty("seasons", out _));
            return Json("""{"id":42,"status":1,"requestedBy":{"email":"private"}}""", HttpStatusCode.Created);
        });
        var result = await Client(handler).RequestAsync(UserId, type, 123, type == "tv" ? [2] : null, default);
        Assert.Equal(new SeerrRequestResult(42, 1), result);
        Assert.Equal(3, handler.Count);
    }

    [Theory]
    [InlineData("movie", 524288)]
    [InlineData("tv", 262144)]
    [InlineData("movie", 4194304)]
    public async Task IssueOrWrongMediaPermissionsCannotCreateRequests(string type, long permissions)
    {
        var handler = new Handler((_, _) => Task.FromResult(User(permissions)));
        var error = await Assert.ThrowsAsync<SeerrException>(() => Client(handler).RequestAsync(UserId, type, 123, type == "tv" ? [2] : null, default));
        Assert.Equal("permission_denied", error.Code);
        Assert.Equal(1, handler.Count);
    }

    [Fact]
    public async Task UnsyncedUserCannotSearchOrRequestAsApiKeyOwner()
    {
        var handler = new Handler((_, _) => Task.FromResult(Json("""{"results":[],"pageInfo":{"results":0}}""")));
        Assert.Equal("user_not_synced", (await Assert.ThrowsAsync<SeerrException>(() => Client(handler).SearchAsync(UserId, "test", 1, default))).Code);
        Assert.Equal("user_not_synced", (await Assert.ThrowsAsync<SeerrException>(() => Client(handler).RequestAsync(UserId, "movie", 123, null, default))).Code);
        Assert.Equal(2, handler.Count);
    }

    [Theory]
    [InlineData(0)] // special is available
    [InlineData(1)] // pending season
    [InlineData(99)] // nonexistent season
    [InlineData(-1)]
    public async Task InvalidOrUnavailableSeasonIsRejectedBeforePosting(int season)
    {
        var handler = new Handler((_, call) => Task.FromResult(call == 1 ? User() : Details("tv")));
        var error = await Assert.ThrowsAsync<SeerrException>(() => Client(handler).RequestAsync(UserId, "tv", 123, [season], default));
        Assert.Equal("invalid_seasons", error.Code);
        Assert.Equal(2, handler.Count);
    }

    [Theory]
    [InlineData(4, 1)]
    [InlineData(5, 1)]
    [InlineData(1, 5)]
    [InlineData(6, 1)]
    public async Task AvailabilityIsRecheckedBeforePosting(int status, int status4k)
    {
        var handler = new Handler((_, call) => Task.FromResult(call == 1 ? User() : Json(JsonSerializer.Serialize(new { id = 123, title = "Now available", mediaInfo = new { status, status4k } }))));
        var error = await Assert.ThrowsAsync<SeerrException>(() => Client(handler).RequestAsync(UserId, "movie", 123, null, default));
        Assert.Equal("already_available", error.Code);
        Assert.Equal(2, handler.Count);
    }

    [Theory]
    [InlineData(HttpStatusCode.Accepted, "request_not_created")]
    [InlineData(HttpStatusCode.Conflict, "already_requested")]
    [InlineData(HttpStatusCode.Forbidden, "permission_denied")]
    [InlineData(HttpStatusCode.TooManyRequests, "rate_limited")]
    public async Task FailedRequestsAreNotRetriedOrFalselyConfirmed(HttpStatusCode status, string code)
    {
        var handler = new Handler((_, call) => Task.FromResult(call switch
        {
            1 => User(), 2 => Details("movie"), _ => Json("""{"error":"private configuration"}""", status)
        }));
        var error = await Assert.ThrowsAsync<SeerrException>(() => Client(handler).RequestAsync(UserId, "movie", 123, null, default));
        Assert.Equal(code, error.Code);
        Assert.DoesNotContain("private configuration", error.ToString());
        Assert.Equal(3, handler.Count);
    }

    [Theory]
    [InlineData("person", 123)]
    [InlineData("../settings", 123)]
    [InlineData("movie", 0)]
    public async Task RejectsUnsupportedMediaBeforeContactingSeerr(string type, int id)
    {
        var handler = new Handler((_, _) => throw new Exception("Should not send"));
        Assert.Equal("unsupported_request", (await Assert.ThrowsAsync<SeerrException>(() => Client(handler).RequestAsync(UserId, type, id, null, default))).Code);
        Assert.Equal(0, handler.Count);
    }

    [Fact]
    public void ResponseContractsDoNotDependOnJellyfinSerializerCasing()
    {
        var media = new SeerrSearchItem(1, "tv", "Title", null, null, "Story", false);
        using var search = JsonDocument.Parse(JsonSerializer.Serialize(new SeerrSearchPage(1, 2, [media])));
        Assert.Equal(2, search.RootElement.GetProperty("totalPages").GetInt32());
        Assert.Equal("tv", search.RootElement.GetProperty("results")[0].GetProperty("mediaType").GetString());
        using var details = JsonDocument.Parse(JsonSerializer.Serialize(new SeerrRequestDetails(media, [new SeerrSeason(2, "Season 2", 8, false, true)])));
        Assert.Equal(1, details.RootElement.GetProperty("media").GetProperty("id").GetInt32());
        Assert.True(details.RootElement.GetProperty("seasons")[0].GetProperty("requested").GetBoolean());
        Assert.Equal(2, details.RootElement.GetProperty("seasons")[0].GetProperty("seasonNumber").GetInt32());
        using var request = JsonDocument.Parse(JsonSerializer.Serialize(new SeerrRequestResult(9, 1)));
        Assert.Equal(9, request.RootElement.GetProperty("id").GetInt32());
        Assert.Equal(1, request.RootElement.GetProperty("status").GetInt32());
    }

    private static HttpResponseMessage Details(string type) => Json(type == "movie"
        ? """{"id":123,"title":"Missing","mediaInfo":{"status":1}}"""
        : """{"id":123,"name":"Missing Show","seasons":[{"seasonNumber":0,"name":"Specials","episodeCount":1},{"seasonNumber":1,"name":"Season 1","episodeCount":8},{"seasonNumber":2,"name":"Season 2","episodeCount":8}],"mediaInfo":{"status":2,"seasons":[{"seasonNumber":0,"status":5},{"seasonNumber":1,"status":2}]}}""");

    private sealed class Handler(Func<HttpRequestMessage, int, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        public int Count { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => send(request, ++Count);
    }
}
