using System.Net;
using System.Net.Http.Headers;
using GitApp.Domain;
using GitApp.GitHub;
using GitApp.Services;

namespace GitApp.Core.Tests;

/// <summary>
/// Notifications, milestone 5.
///
/// The polling rules are the part that has to be right and the part a live
/// account will not show you on demand: a 304 that must not be read as an
/// error, an X-Poll-Interval that must not be undercut, a rate limit that must
/// widen the gap rather than close it. They are faked here and the behaviour is
/// asserted. The row wording is asserted too, because the wording is what the
/// listener hears.
/// </summary>
public class NotificationTests
{
    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

        public FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) =>
            _respond = respond;

        public List<HttpRequestMessage> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(_respond(request));
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };

    private const string OneReviewRequest = """
    [
      {
        "id": "1001",
        "unread": true,
        "reason": "review_requested",
        "updated_at": "2026-09-11T10:00:00Z",
        "subject": {
          "title": "Fix the parser crash",
          "url": "https://api.github.com/repos/G4p-Studios/GitApp/pulls/42",
          "type": "PullRequest"
        },
        "repository": { "full_name": "G4p-Studios/GitApp" }
      }
    ]
    """;

    // -----------------------------------------------------------------
    // Parsing
    // -----------------------------------------------------------------

    [Fact]
    public void ParsesTheFieldsTheInboxShows()
    {
        var items = GitHubClient.ParseNotifications(OneReviewRequest);

        var item = Assert.Single(items);
        Assert.Equal("1001", item.Id);
        Assert.Equal("Fix the parser crash", item.Title);
        Assert.Equal("G4p-Studios/GitApp", item.RepositoryFullName);
        Assert.Equal(GitHubSubjectKind.PullRequest, item.SubjectKind);
        Assert.Equal("review_requested", item.Reason);
        Assert.True(item.Unread);
        Assert.Equal(2026, item.UpdatedAt.Year);
    }

    [Fact]
    public void AnUnknownSubjectTypeStaysReadableRatherThanVanishing()
    {
        var items = GitHubClient.ParseNotifications("""
        [{ "id": "7", "subject": { "title": "A thing", "type": "CheckSuite" } }]
        """);

        var item = Assert.Single(items);
        Assert.Equal(GitHubSubjectKind.Other, item.SubjectKind);
        Assert.Equal("notification", item.SubjectWord);
    }

    [Fact]
    public void AnEntryWithNoTitleIsSkippedRatherThanAnnouncedBlank()
    {
        // A titleless row would read as just its reason and repository, which
        // sounds like a glitch and cannot be acted on.
        var items = GitHubClient.ParseNotifications("""
        [{ "id": "1", "subject": { "type": "Issue" } }, { "id": "2", "subject": { "title": "real", "type": "Issue" } }]
        """);

        Assert.Equal("real", Assert.Single(items).Title);
    }

    [Fact]
    public void MissingFieldsDoNotThrow()
    {
        var items = GitHubClient.ParseNotifications("""[{ "id": "9", "subject": { "title": "bare" } }]""");

        var item = Assert.Single(items);
        Assert.Equal(string.Empty, item.RepositoryFullName);
        Assert.Equal(GitHubSubjectKind.Other, item.SubjectKind);
        Assert.False(item.Unread);
    }

    // -----------------------------------------------------------------
    // Wording, which is the feature
    // -----------------------------------------------------------------

    [Fact]
    public void TheRowLeadsWithTheTitleThenWhyThenWhatThenWhereThenWhen()
    {
        var item = Assert.Single(GitHubClient.ParseNotifications(OneReviewRequest));

        Assert.Equal(
            "Fix the parser crash, review requested, pull request, in G4p-Studios/GitApp, "
            + RelativeTime.From(item.UpdatedAt),
            item.AccessibleName);
    }

    [Fact]
    public void UnreadIsNotFoldedIntoTheRowName()
    {
        // ARCHITECTURE 3.3: state that ticks underneath a row must not live in
        // the name, or the row is re-read every time it flips. It rides a
        // separate element instead.
        var item = Assert.Single(GitHubClient.ParseNotifications(OneReviewRequest));

        Assert.DoesNotContain("unread", item.AccessibleName);
        Assert.Equal("unread", item.UnreadIndicator);
    }

    [Theory]
    [InlineData("review_requested", "review requested")]
    [InlineData("mention", "you were mentioned")]
    [InlineData("state_change", "closed or reopened")]
    [InlineData("team_mention", "your team was mentioned")]
    public void ReasonsAreSpokenAsWordsNotTokens(string reason, string expected)
    {
        Assert.Equal(expected, NotificationReason.Word(reason));
    }

    [Fact]
    public void AnUnknownReasonSpeaksItsUnderscoresAsSpaces()
    {
        // Not "reminder underscore due"; the underscore is not something a
        // listener can act on.
        Assert.Equal("reminder due", NotificationReason.Word("reminder_due"));
    }

    [Theory]
    [InlineData("https://api.github.com/repos/G4p-Studios/GitApp/issues/42",
                "https://github.com/G4p-Studios/GitApp/issues/42")]
    [InlineData("https://api.github.com/repos/G4p-Studios/GitApp/pulls/7",
                "https://github.com/G4p-Studios/GitApp/pull/7")]
    [InlineData("https://api.github.com/repos/G4p-Studios/GitApp/commits/abc123",
                "https://github.com/G4p-Studios/GitApp/commit/abc123")]
    public void ApiUrlsBecomeWebUrls(string api, string expected)
    {
        Assert.Equal(expected, NotificationLinks.Web(api, "G4p-Studios/GitApp"));
    }

    [Fact]
    public void AnUnmappableSubjectFallsBackToTheRepositoryPage()
    {
        // A release's API url is keyed on an id, not the tag its web page uses,
        // so opening the repository is the honest answer.
        Assert.Equal(
            "https://github.com/G4p-Studios/GitApp",
            NotificationLinks.Web(
                "https://api.github.com/repos/G4p-Studios/GitApp/releases/99", "G4p-Studios/GitApp"));

        Assert.Equal(
            "https://github.com/G4p-Studios/GitApp",
            NotificationLinks.Web(null, "G4p-Studios/GitApp"));
    }

    [Fact]
    public void TheInboxHeadingCountsUnreadInWords()
    {
        var page = new NotificationPage(
            new[]
            {
                Sample("1", unread: true),
                Sample("2", unread: true),
                Sample("3", unread: false),
            },
            PollToken: null,
            PollIntervalSeconds: 60,
            NotModified: false);

        Assert.Equal("2 unread notifications", page.InboxHeading);
    }

    // -----------------------------------------------------------------
    // Conditional polling
    // -----------------------------------------------------------------

    [Fact]
    public async Task ThePollTokenComesBackFromLastModified()
    {
        var handler = new FakeHandler(_ =>
        {
            var response = Json(HttpStatusCode.OK, OneReviewRequest);
            response.Content.Headers.LastModified = DateTimeOffset.Parse("2026-09-11T10:00:00Z");
            return response;
        });

        using var client = new GitHubClient("t", handler);
        var result = await client.GetNotificationsAsync();

        Assert.True(result.Success);
        Assert.False(result.Value!.NotModified);
        Assert.False(string.IsNullOrEmpty(result.Value.PollToken));
    }

    [Fact]
    public async Task AKnownTokenIsSentBackAsIfModifiedSince()
    {
        var handler = new FakeHandler(_ => Json(HttpStatusCode.OK, "[]"));

        using var client = new GitHubClient("t", handler);
        await client.GetNotificationsAsync(pollToken: "Wed, 11 Sep 2026 10:00:00 GMT");

        var sent = Assert.Single(handler.Requests);
        Assert.True(sent.Headers.TryGetValues("If-Modified-Since", out var values));
        Assert.Equal("Wed, 11 Sep 2026 10:00:00 GMT", values.First());
    }

    [Fact]
    public async Task NotModifiedIsSuccessAndKeepsTheToken()
    {
        // 304 is the point of the conditional request, not an error: the inbox
        // is unchanged and it did not cost any rate limit.
        var handler = new FakeHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.NotModified);
            response.Headers.TryAddWithoutValidation("X-Poll-Interval", "90");
            return response;
        });

        using var client = new GitHubClient("t", handler);
        var result = await client.GetNotificationsAsync(pollToken: "some-date");

        Assert.True(result.Success);
        Assert.True(result.Value!.NotModified);
        Assert.Empty(result.Value.Items);
        Assert.Equal("some-date", result.Value.PollToken);
        Assert.Equal(90, result.Value.PollIntervalSeconds);
    }

    [Fact]
    public async Task ThePollIntervalIsCarriedOutOfTheHeader()
    {
        var handler = new FakeHandler(_ =>
        {
            var response = Json(HttpStatusCode.OK, "[]");
            response.Headers.TryAddWithoutValidation("X-Poll-Interval", "120");
            return response;
        });

        using var client = new GitHubClient("t", handler);
        var result = await client.GetNotificationsAsync();

        Assert.Equal(120, result.Value!.PollIntervalSeconds);
    }

    [Fact]
    public async Task ARateLimitedPollSaysHowLongToWait()
    {
        var reset = DateTimeOffset.UtcNow.AddMinutes(9).ToUnixTimeSeconds();

        using var client = new GitHubClient("t", new FakeHandler(_ =>
        {
            var response = Json(HttpStatusCode.Forbidden, """{"message":"rate limit"}""");
            response.Headers.Add("X-RateLimit-Remaining", "0");
            response.Headers.Add("X-RateLimit-Reset", reset.ToString());
            return response;
        }));

        var result = await client.GetNotificationsAsync();

        Assert.False(result.Success);
        Assert.Equal(GitHubFailure.RateLimited, result.Failure);
        Assert.Contains("9 minutes", result.Error!);
    }

    [Fact]
    public async Task OfflineIsSaidPlainlyRatherThanThrown()
    {
        using var client = new GitHubClient("t", new FakeHandler(
            _ => throw new HttpRequestException("no such host")));

        var result = await client.GetNotificationsAsync();

        Assert.Equal(GitHubFailure.Offline, result.Failure);
        Assert.Contains("internet connection", result.Error!);
    }

    [Fact]
    public async Task MarkingAThreadReadAcceptsGitHubsEmptyReply()
    {
        // GitHub answers 205 Reset Content with no body.
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.ResetContent));

        using var client = new GitHubClient("t", handler);
        var result = await client.MarkThreadReadAsync("1001");

        Assert.True(result.Success);
        var sent = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Patch, sent.Method);
        Assert.EndsWith("/notifications/threads/1001", sent.RequestUri!.AbsolutePath);
    }

    // -----------------------------------------------------------------
    // The poll scheduler
    // -----------------------------------------------------------------

    [Fact]
    public void TheFirstPollIsDueImmediately()
    {
        var poller = new NotificationPoller();

        Assert.True(poller.IsDue(DateTimeOffset.UnixEpoch));
    }

    [Fact]
    public void ThePollIntervalIsHonouredAsAFloor()
    {
        var poller = new NotificationPoller(minIntervalSeconds: 60);
        var now = DateTimeOffset.UnixEpoch;

        // GitHub asks for 120; the larger of the two wins.
        poller.Observe(new NotificationPage(Array.Empty<GitHubNotification>(), "t", 120, NotModified: true), now);

        Assert.Equal(120, poller.LastIntervalSeconds);
        Assert.False(poller.IsDue(now.AddSeconds(119)));
        Assert.True(poller.IsDue(now.AddSeconds(120)));
    }

    [Fact]
    public void OurOwnFloorWinsWhenGitHubAsksForLess()
    {
        var poller = new NotificationPoller(minIntervalSeconds: 60);
        var now = DateTimeOffset.UnixEpoch;

        poller.Observe(new NotificationPage(Array.Empty<GitHubNotification>(), "t", 15, NotModified: true), now);

        Assert.Equal(60, poller.LastIntervalSeconds);
    }

    [Fact]
    public void OnlyNewUnreadNotificationsAreWorthAnnouncing()
    {
        var poller = new NotificationPoller();
        var now = DateTimeOffset.UnixEpoch;

        var first = poller.Observe(Page(Sample("1", unread: true), Sample("2", unread: true)), now);
        Assert.Equal(2, first.Count);

        // The same two come back next poll; neither is new any more.
        var second = poller.Observe(Page(Sample("1", unread: true), Sample("2", unread: true)), now.AddMinutes(2));
        Assert.Empty(second);

        // A third arrives.
        var third = poller.Observe(
            Page(Sample("1", unread: true), Sample("2", unread: true), Sample("3", unread: true)),
            now.AddMinutes(4));
        Assert.Equal("3", Assert.Single(third).Id);
    }

    [Fact]
    public void AThreadThatChangedIsAnnouncedAgain()
    {
        var poller = new NotificationPoller();
        var now = DateTimeOffset.UnixEpoch;
        var when = DateTimeOffset.Parse("2026-09-11T10:00:00Z");

        poller.Observe(Page(Sample("1", unread: true, updatedAt: when)), now);

        // Same id, later timestamp: a new comment on a thread already seen.
        var again = poller.Observe(Page(Sample("1", unread: true, updatedAt: when.AddMinutes(5))), now.AddMinutes(2));

        Assert.Equal("1", Assert.Single(again).Id);
    }

    [Fact]
    public void AReadThreadIsNotAnnouncedEvenWhenNew()
    {
        var poller = new NotificationPoller();

        var fresh = poller.Observe(Page(Sample("1", unread: false)), DateTimeOffset.UnixEpoch);

        Assert.Empty(fresh);
    }

    [Fact]
    public void ARateLimitWidensTheGapEachTime()
    {
        var poller = new NotificationPoller(minIntervalSeconds: 60);
        var now = DateTimeOffset.UnixEpoch;

        poller.Backoff(now, GitHubFailure.RateLimited);
        var first = poller.LastIntervalSeconds;

        poller.Backoff(now, GitHubFailure.RateLimited);
        var second = poller.LastIntervalSeconds;

        Assert.True(second > first);
        Assert.True(second <= 3600);
    }

    [Fact]
    public void BackoffCapsAtAnHour()
    {
        var poller = new NotificationPoller(minIntervalSeconds: 60);
        var now = DateTimeOffset.UnixEpoch;

        for (var i = 0; i < 20; i++)
        {
            poller.Backoff(now, GitHubFailure.RateLimited);
        }

        Assert.Equal(3600, poller.LastIntervalSeconds);
    }

    [Fact]
    public void BeingOfflineJustWaitsTheNormalFloor()
    {
        var poller = new NotificationPoller(minIntervalSeconds: 60);

        poller.Backoff(DateTimeOffset.UnixEpoch, GitHubFailure.Offline);

        Assert.Equal(60, poller.LastIntervalSeconds);
    }

    [Fact]
    public void ASuccessfulPollResetsTheBackoff()
    {
        var poller = new NotificationPoller(minIntervalSeconds: 60);
        var now = DateTimeOffset.UnixEpoch;

        poller.Backoff(now, GitHubFailure.RateLimited);
        poller.Backoff(now, GitHubFailure.RateLimited);
        poller.Observe(new NotificationPage(Array.Empty<GitHubNotification>(), "t", 60, NotModified: true), now);

        // Next rate limit starts from the bottom of the ramp again.
        poller.Backoff(now, GitHubFailure.RateLimited);
        Assert.Equal(120, poller.LastIntervalSeconds);
    }

    [Fact]
    public void ObserveCarriesTheTokenForwardEvenOnA304()
    {
        var poller = new NotificationPoller();
        var now = DateTimeOffset.UnixEpoch;

        poller.Observe(new NotificationPage(Array.Empty<GitHubNotification>(), "the-token", 60, NotModified: true), now);

        Assert.Equal("the-token", poller.PollToken);
    }

    private static NotificationPage Page(params GitHubNotification[] items) =>
        new(items, "token", 60, NotModified: false);

    private static GitHubNotification Sample(
        string id, bool unread = true, DateTimeOffset? updatedAt = null) =>
        new(
            Id: id,
            RepositoryFullName: "G4p-Studios/GitApp",
            Title: $"Notification {id}",
            SubjectKind: GitHubSubjectKind.Issue,
            Reason: "subscribed",
            Unread: unread,
            UpdatedAt: updatedAt ?? DateTimeOffset.Parse("2026-09-11T10:00:00Z"));
}
