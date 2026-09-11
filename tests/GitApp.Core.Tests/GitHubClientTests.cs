using System.Net;
using GitApp.GitHub;

namespace GitApp.Core.Tests;

/// <summary>
/// The error paths, mostly.
///
/// A live account never shows you a revoked token, a rate limit or a
/// truncated reply, and those are exactly the moments when a screen reader
/// user is left with nothing to act on. So they are faked here and the
/// wording is asserted, because the wording is the feature.
/// </summary>
public class GitHubClientTests
{
    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

        public FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) =>
            _respond = respond;

        public List<string> Requested { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requested.Add(request.RequestUri!.ToString());
            return Task.FromResult(_respond(request));
        }
    }

    /// <summary>
    /// Careful: "per_page=100" contains "page=1", so a substring match says
    /// every request is the first page and the paging test passes while
    /// testing nothing. Match the parameter, not the text.
    /// </summary>
    private static bool IsPage(HttpRequestMessage request, int page) =>
        request.RequestUri!.Query.EndsWith($"page={page}", StringComparison.Ordinal);

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };

    // -----------------------------------------------------------------
    // Parsing
    // -----------------------------------------------------------------

    private const string TwoRepos = """
    [
      {
        "name": "GitApp",
        "owner": { "login": "G4p-Studios" },
        "description": "Git made super simple, yet so powerful",
        "private": false,
        "fork": false,
        "language": "C#",
        "stargazers_count": 3,
        "pushed_at": "2026-09-11T04:00:00Z",
        "clone_url": "https://github.com/G4p-Studios/GitApp.git",
        "html_url": "https://github.com/G4p-Studios/GitApp"
      },
      {
        "name": "secret-notes",
        "owner": { "login": "alexoloopios" },
        "description": null,
        "private": true,
        "fork": true,
        "language": null,
        "stargazers_count": 0,
        "updated_at": "2026-09-10T04:00:00Z",
        "clone_url": "https://github.com/alexoloopios/secret-notes.git",
        "html_url": "https://github.com/alexoloopios/secret-notes"
      }
    ]
    """;

    [Fact]
    public void ParsesTheFieldsTheListShows()
    {
        var repos = GitHubClient.Parse(TwoRepos);

        Assert.Equal(2, repos.Count);
        Assert.Equal("GitApp", repos[0].Name);
        Assert.Equal("G4p-Studios", repos[0].Owner);
        Assert.Equal("C#", repos[0].Language);
        Assert.Equal(3, repos[0].Stars);
        Assert.False(repos[0].IsPrivate);
        Assert.True(repos[1].IsPrivate);
        Assert.True(repos[1].IsFork);
        Assert.Null(repos[1].Description);
    }

    [Fact]
    public void MissingAndNullFieldsDoNotThrow()
    {
        var repos = GitHubClient.Parse("""[{ "name": "bare" }]""");

        var repo = Assert.Single(repos);
        Assert.Equal("bare", repo.Name);
        Assert.Equal(string.Empty, repo.Owner);
        Assert.Null(repo.UpdatedAt);
        Assert.Equal(0, repo.Stars);
    }

    [Fact]
    public void AnEntryWithNoNameIsSkippedRatherThanListedBlank()
    {
        // A nameless row would be announced as just its metadata, which
        // reads as a glitch and cannot be acted on.
        var repos = GitHubClient.Parse("""[{ "private": true }, { "name": "real" }]""");

        Assert.Equal("real", Assert.Single(repos).Name);
    }

    [Fact]
    public void PushedAtWinsOverUpdatedAt()
    {
        // "Updated" on github.com means pushed. updated_at also moves when
        // someone stars the repository, which is not what the user means.
        var repos = GitHubClient.Parse("""
        [{ "name": "x", "pushed_at": "2026-09-11T04:00:00Z", "updated_at": "2020-01-01T00:00:00Z" }]
        """);

        Assert.Equal(2026, repos[0].UpdatedAt!.Value.Year);
    }

    // -----------------------------------------------------------------
    // Failures, and how they are worded
    // -----------------------------------------------------------------

    [Fact]
    public async Task ARevokedTokenSaysToSignInAgain()
    {
        using var client = new GitHubClient("dead", new FakeHandler(
            _ => Json(HttpStatusCode.Unauthorized, """{"message":"Bad credentials"}""")));

        var result = await client.GetAccountAsync();

        Assert.False(result.Success);
        Assert.Equal(GitHubFailure.Unauthenticated, result.Failure);
        Assert.Contains("rejected that token", result.Error!);
    }

    [Fact]
    public async Task ARateLimitSaysHowLongToWait()
    {
        var reset = DateTimeOffset.UtcNow.AddMinutes(12).ToUnixTimeSeconds();

        using var client = new GitHubClient("t", new FakeHandler(_ =>
        {
            var response = Json(HttpStatusCode.Forbidden, """{"message":"rate limit"}""");
            response.Headers.Add("X-RateLimit-Remaining", "0");
            response.Headers.Add("X-RateLimit-Reset", reset.ToString());
            return response;
        }));

        var result = await client.GetAccountAsync();

        Assert.Equal(GitHubFailure.RateLimited, result.Failure);
        Assert.Contains("12 minutes", result.Error!);
    }

    [Fact]
    public async Task APlainForbiddenIsNotMistakenForARateLimit()
    {
        using var client = new GitHubClient("t", new FakeHandler(
            _ => Json(HttpStatusCode.Forbidden, """{"message":"nope"}""")));

        var result = await client.GetAccountAsync();

        Assert.Equal(GitHubFailure.Forbidden, result.Failure);
        Assert.Contains("repo scope", result.Error!);
    }

    [Fact]
    public async Task NoNetworkSaysSoRatherThanShowingAnException()
    {
        using var client = new GitHubClient("t", new FakeHandler(
            _ => throw new HttpRequestException("no such host")));

        var result = await client.GetAccountAsync();

        Assert.Equal(GitHubFailure.Offline, result.Failure);
        Assert.Contains("internet connection", result.Error!);
        Assert.DoesNotContain("Exception", result.Error!);
    }

    [Fact]
    public async Task AMangledReplyIsReportedRatherThanCrashing()
    {
        using var client = new GitHubClient("t", new FakeHandler(
            _ => Json(HttpStatusCode.OK, "{ not json")));

        var result = await client.GetAccountAsync();

        Assert.False(result.Success);
        Assert.Contains("could not read", result.Error!);
    }

    // -----------------------------------------------------------------
    // Paging
    // -----------------------------------------------------------------

    [Fact]
    public async Task AFullPageIsFollowedByAnotherRequest()
    {
        var full = "[" + string.Join(",",
            Enumerable.Range(0, 100).Select(i => $$"""{ "name": "r{{i}}" }""")) + "]";

        var handler = new FakeHandler(request =>
            Json(HttpStatusCode.OK, IsPage(request, 1) ? full : """[{"name":"last"}]"""));

        using var client = new GitHubClient("t", handler);
        var result = await client.GetRepositoriesAsync();

        Assert.True(result.Success);
        Assert.Equal(101, result.Value!.Count);
        Assert.Equal(2, handler.Requested.Count);
    }

    [Fact]
    public async Task AShortFirstPageEndsTheListing()
    {
        var handler = new FakeHandler(_ => Json(HttpStatusCode.OK, TwoRepos));

        using var client = new GitHubClient("t", handler);
        var result = await client.GetRepositoriesAsync();

        Assert.Equal(2, result.Value!.Count);
        Assert.Single(handler.Requested);
    }

    [Fact]
    public async Task FailurePartWayThroughPagingIsReportedNotSilentlyTruncated()
    {
        var full = "[" + string.Join(",",
            Enumerable.Range(0, 100).Select(i => $$"""{ "name": "r{{i}}" }""")) + "]";

        var handler = new FakeHandler(request =>
            IsPage(request, 1)
                ? Json(HttpStatusCode.OK, full)
                : Json(HttpStatusCode.ServiceUnavailable, "{}"));

        using var client = new GitHubClient("t", handler);
        var result = await client.GetRepositoriesAsync();

        // A half-loaded list that claims to be complete is worse than an
        // error: the repository you wanted is simply absent.
        Assert.False(result.Success);
        Assert.Contains("GitHub is having trouble", result.Error!);
    }
}
