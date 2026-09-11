using System.Net;
using GitApp.GitHub;

namespace GitApp.Core.Tests;

public class GitHubSessionTests
{
    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _body;

        public FakeHandler(HttpStatusCode status, string body)
        {
            _status = status;
            _body = body;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(_status)
            {
                Content = new StringContent(_body, System.Text.Encoding.UTF8, "application/json"),
            });
    }

    private static GitHubSession Session(
        ITokenStore store, HttpStatusCode status, string body) =>
        new(store, (token, _) => new GitHubClient(token, new FakeHandler(status, body)));

    private const string Viewer = """{ "login": "alexoloopios", "name": "Alex Chapman" }""";

    [Fact]
    public async Task SigningInStoresTheTokenAndNamesTheAccount()
    {
        var store = new InMemoryTokenStore();
        var session = Session(store, HttpStatusCode.OK, Viewer);

        var result = await session.SignInWithTokenAsync("  ghp_secret  ");

        Assert.True(result.Success);
        Assert.True(session.IsSignedIn);
        Assert.Equal("Signed in as Alex Chapman, alexoloopios", session.StatusDescription);

        // Trimmed, because a token pasted from a web page usually arrives
        // with whitespace and the failure would look like a bad token.
        Assert.Equal("ghp_secret", await store.GetAsync("github.com"));
    }

    [Fact]
    public async Task AnEmptyTokenIsRefusedWithoutACall()
    {
        var session = Session(new InMemoryTokenStore(), HttpStatusCode.OK, Viewer);

        var result = await session.SignInWithTokenAsync("   ");

        Assert.False(result.Success);
        Assert.Contains("Enter a token", result.Error!);
    }

    [Fact]
    public async Task ARejectedTokenIsNotStored()
    {
        var store = new InMemoryTokenStore();
        var session = Session(store, HttpStatusCode.Unauthorized, """{"message":"Bad credentials"}""");

        var result = await session.SignInWithTokenAsync("ghp_wrong");

        Assert.False(result.Success);
        Assert.False(session.IsSignedIn);
        Assert.Null(await store.GetAsync("github.com"));
    }

    [Fact]
    public async Task ARevokedStoredTokenIsClearedOnRestore()
    {
        var store = new InMemoryTokenStore();
        await store.SetAsync("github.com", "ghp_revoked");

        var session = Session(store, HttpStatusCode.Unauthorized, """{"message":"Bad credentials"}""");
        await session.RestoreAsync();

        // Otherwise every call for the rest of the session fails with an
        // error the user cannot act on.
        Assert.Null(await store.GetAsync("github.com"));
    }

    [Fact]
    public async Task BeingOfflineDoesNotThrowAwayTheStoredToken()
    {
        var store = new InMemoryTokenStore();
        await store.SetAsync("github.com", "ghp_good");

        var session = new GitHubSession(store, (token, _) =>
            new GitHubClient(token, new ThrowingHandler()));

        var result = await session.RestoreAsync();

        Assert.Equal(GitHubFailure.Offline, result.Failure);
        Assert.Equal("ghp_good", await store.GetAsync("github.com"));
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("offline");
    }

    [Fact]
    public async Task SigningOutForgetsTheToken()
    {
        var store = new InMemoryTokenStore();
        var session = Session(store, HttpStatusCode.OK, Viewer);
        await session.SignInWithTokenAsync("ghp_secret");

        await session.SignOutAsync();

        Assert.False(session.IsSignedIn);
        Assert.Equal("Not signed in to GitHub", session.StatusDescription);
        Assert.Null(await store.GetAsync("github.com"));
        Assert.Null(session.CreateClient());
    }

    [Fact]
    public async Task RestoringWithNothingStoredSaysSignedOutRatherThanFailing()
    {
        var session = Session(new InMemoryTokenStore(), HttpStatusCode.OK, Viewer);

        var result = await session.RestoreAsync();

        Assert.Equal(GitHubFailure.Unauthenticated, result.Failure);
        Assert.False(session.IsSignedIn);
    }

    [Fact]
    public void BrowserSignInIsOffWhenThereIsNoClientId()
    {
        Assert.False(new GitHubSession().CanUseBrowserSignIn);
        Assert.True(new GitHubSession(deviceFlowClientId: "Iv1.abc").CanUseBrowserSignIn);
    }

    [Fact]
    public async Task AnAccountWithNoDisplayNameFallsBackToTheLogin()
    {
        var session = Session(new InMemoryTokenStore(), HttpStatusCode.OK, """{ "login": "ghost" }""");

        await session.SignInWithTokenAsync("t");

        Assert.Equal("Signed in as ghost", session.StatusDescription);
    }
}
