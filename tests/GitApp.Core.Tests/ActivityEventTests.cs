using System.Net;
using GitApp.Domain;
using GitApp.GitHub;

namespace GitApp.Core.Tests;

/// <summary>
/// The Home feed: sentences a listener can act on, and the client that
/// fetches them. Heard wording is the product (ARCHITECTURE 3.3).
/// </summary>
public class ActivityEventTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 14, 4, 0, 0, TimeSpan.Zero);

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

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };

    private const string SampleFeed = """
    [
      {
        "id": "1",
        "type": "WatchEvent",
        "actor": { "login": "jage9" },
        "repo": { "name": "Athlon/TheGreatAthlon" },
        "payload": { "action": "started" },
        "created_at": "2026-09-14T03:49:00Z"
      },
      {
        "id": "2",
        "type": "PullRequestEvent",
        "actor": { "login": "octocat" },
        "repo": { "name": "G4p-Studios/GitApp" },
        "payload": {
          "action": "opened",
          "pull_request": { "title": "Fix the parser crash", "number": 12 }
        },
        "created_at": "2026-09-14T02:00:00Z"
      },
      {
        "id": "3",
        "type": "PushEvent",
        "actor": { "login": "alexoloopios" },
        "repo": { "name": "icg-community/site" },
        "payload": { "ref": "refs/heads/main", "size": 2 },
        "created_at": "2026-09-13T04:00:00Z"
      },
      {
        "id": "4",
        "type": "CreateEvent",
        "actor": { "login": "octocat" },
        "repo": { "name": "octocat/hello" },
        "payload": { "ref_type": "repository" },
        "created_at": "2026-09-12T04:00:00Z"
      },
      {
        "id": "5",
        "type": "SomeNewEvent",
        "actor": { "login": "newbie" },
        "repo": { "name": "newbie/lab" },
        "payload": {},
        "created_at": "2026-09-11T04:00:00Z"
      }
    ]
    """;

    [Fact]
    public void StarLeadsWithTheActorThenTheRepository()
    {
        var sentence = ActivityEvent.Sentence(
            "WatchEvent", "jage9", "Athlon/TheGreatAthlon", "started", null, null, null, null);

        Assert.Equal("jage9 starred Athlon/TheGreatAthlon", sentence);
    }

    [Fact]
    public void PullRequestIncludesTheTitleAsWords()
    {
        var sentence = ActivityEvent.Sentence(
            "PullRequestEvent", "octocat", "G4p-Studios/GitApp", "opened",
            null, null, "Fix the parser crash", 12);

        Assert.Equal(
            "octocat opened pull request Fix the parser crash, number 12, in G4p-Studios/GitApp",
            sentence);
    }

    [Fact]
    public void PushSpeaksTheBranchNotTheRef()
    {
        var sentence = ActivityEvent.Sentence(
            "PushEvent", "alexoloopios", "icg-community/site", null, null,
            "refs/heads/main", null, null);

        Assert.Equal("alexoloopios pushed to main in icg-community/site", sentence);
    }

    [Fact]
    public void UnknownTypesStillReadAsWords()
    {
        var sentence = ActivityEvent.Sentence(
            "SomeNewEvent", "newbie", "newbie/lab", null, null, null, null, null);

        Assert.Equal("newbie some new in newbie/lab", sentence);
    }

    [Fact]
    public void AccessibleNamePutsWhenLast()
    {
        var ev = new ActivityEvent(
            "1", "WatchEvent", "jage9", "Athlon/TheGreatAthlon",
            "jage9 starred Athlon/TheGreatAthlon",
            DateTimeOffset.UtcNow.AddMinutes(-11),
            "https://github.com/Athlon/TheGreatAthlon");

        Assert.StartsWith("jage9 starred Athlon/TheGreatAthlon, ", ev.AccessibleName);
        Assert.DoesNotContain("WatchEvent", ev.AccessibleName, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseEventsMapsTheFieldsTheFeedShows()
    {
        var events = GitHubClient.ParseEvents(SampleFeed);

        Assert.Equal(5, events.Count);
        Assert.Equal("jage9 starred Athlon/TheGreatAthlon", events[0].Summary);
        Assert.Equal("Athlon", events[0].Owner);
        Assert.Equal("TheGreatAthlon", events[0].RepoName);
        Assert.Equal(
            "octocat opened pull request Fix the parser crash, number 12, in G4p-Studios/GitApp",
            events[1].Summary);
        Assert.Equal("alexoloopios pushed to main in icg-community/site", events[2].Summary);
        Assert.Equal("octocat created the repository octocat/hello", events[3].Summary);
        Assert.Equal("newbie some new in newbie/lab", events[4].Summary);
    }

    [Fact]
    public async Task ReceivedEventsAsksTheDashboardEndpoint()
    {
        var handler = new FakeHandler(_ => Json(HttpStatusCode.OK, SampleFeed));
        using var client = new GitHubClient("token", handler);

        var result = await client.GetReceivedEventsAsync("alexoloopios");

        Assert.True(result.Success);
        Assert.Equal(5, result.Value!.Count);
        Assert.Contains(
            "https://api.github.com/users/alexoloopios/received_events?per_page=30",
            handler.Requested);
    }

    [Fact]
    public async Task ReceivedEventsWithoutALoginDoesNotHitTheNetwork()
    {
        var handler = new FakeHandler(_ => throw new InvalidOperationException("should not run"));
        using var client = new GitHubClient("token", handler);

        var result = await client.GetReceivedEventsAsync("  ");

        Assert.False(result.Success);
        Assert.Empty(handler.Requested);
    }

    [Fact]
    public void PaletteFilterIsASubstringAndEmptyQueryKeepsEverything()
    {
        var commands = new[]
        {
            new PaletteCommand("Local repositories", "Go"),
            new PaletteCommand("Notifications", "Go"),
            new PaletteCommand("Clone a repository", "Local"),
        };

        Assert.Equal(3, PaletteFilter.Match(commands, "").Count);
        Assert.Equal("Notifications", Assert.Single(PaletteFilter.Match(commands, "notif")).Title);
        Assert.Equal(2, PaletteFilter.Match(commands, "reposit").Count);
        Assert.Equal(2, PaletteFilter.Match(commands, "local").Count);
    }
}
