using System.Net;
using System.Net.Http;
using System.Text;
using GitApp.Domain;
using GitApp.GitHub;

namespace GitApp.Core.Tests;

public class RepositoryViewTests
{
    private const string Snapshot = """
        {
          "data": {
            "repository": {
              "name": "GitApp",
              "description": "Git made super simple, yet so powerful",
              "isPrivate": false,
              "stargazerCount": 3,
              "forkCount": 0,
              "homepageUrl": null,
              "url": "https://github.com/G4p-Studios/GitApp",
              "owner": { "login": "G4p-Studios" },
              "primaryLanguage": { "name": "C#" },
              "watchers": { "totalCount": 1 },
              "repositoryTopics": {
                "nodes": [
                  { "topic": { "name": "accessibility" } },
                  { "topic": { "name": "git" } }
                ]
              },
              "defaultBranchRef": { "name": "main" },
              "refs": {
                "totalCount": 2,
                "nodes": [ { "name": "main" }, { "name": "feature" } ]
              },
              "tags": { "totalCount": 0 },
              "releases": { "totalCount": 0 },
              "object": {
                "oid": "1aabfb0abcde",
                "abbreviatedOid": "1aabfb0",
                "messageHeadline": "Add the accessible diff viewer",
                "committedDate": "2026-09-10T12:00:00Z",
                "authors": {
                  "nodes": [
                    { "name": "Alex", "user": { "login": "alexoloopios" } },
                    { "name": "Claude", "user": { "login": "claude" } }
                  ]
                },
                "history": { "totalCount": 17 }
              },
              "tree": {
                "entries": [
                  { "name": "README.md", "type": "blob", "path": "README.md" },
                  { "name": "src", "type": "tree", "path": "src" },
                  { "name": "docs", "type": "tree", "path": "docs" },
                  { "name": "AGENTS.md", "type": "blob", "path": "AGENTS.md" }
                ]
              }
            }
          }
        }
        """;

    [Fact]
    public void ParsesTheFieldsTheScreenShows()
    {
        var view = GitHubClient.ParseRepository(Snapshot);

        Assert.NotNull(view);
        Assert.Equal("GitApp", view!.Name);
        Assert.Equal("G4p-Studios", view.Owner);
        Assert.Equal("C#", view.Language);
        Assert.Equal(3, view.Stars);
        Assert.Equal(1, view.Watchers);
        Assert.Equal(new[] { "accessibility", "git" }, view.Topics);
        Assert.Equal("main", view.CurrentBranch);
        Assert.Equal(2, view.BranchCount);
        Assert.Equal(0, view.ReleaseCount);
        Assert.Contains("G4p-Studios/GitApp, public", view.AboutFacts);
        Assert.Contains("Topics, accessibility, git", view.AboutFacts);
        Assert.Contains("No releases", view.AboutFacts);
    }

    [Fact]
    public void LatestCommitLeadsWithTheSubjectAndNamesBothAuthors()
    {
        var view = GitHubClient.ParseRepository(Snapshot);
        var spoken = view!.Latest!.AccessibleName;

        Assert.StartsWith("Add the accessible diff viewer", spoken);
        Assert.Contains("by alexoloopios and claude", spoken);
        Assert.Contains("1aabfb0", spoken);
        Assert.Contains("17 commits on main", spoken);
        Assert.DoesNotContain("+", spoken);
    }

    [Fact]
    public void DirectoriesComeFirstThenFilesAlphabetically()
    {
        var view = GitHubClient.ParseRepository(Snapshot);
        var names = view!.Entries.Select(e => e.Name).ToArray();

        Assert.Equal(new[] { "docs", "src", "AGENTS.md", "README.md" }, names);
        Assert.Equal(GitHubEntryKind.Folder, view.Entries[0].Kind);
        Assert.Equal(GitHubEntryKind.File, view.Entries[2].Kind);
    }

    [Fact]
    public void ASubfolderGetsAParentRowSoThereIsSomewhereToGoBackTo()
    {
        var view = GitHubClient.ParseRepository(Snapshot, path: "src/GitApp");

        Assert.Equal(GitHubEntryKind.Parent, view!.Entries[0].Kind);
        Assert.Equal("Parent folder", view.Entries[0].AccessibleName);
        Assert.Equal("src", view.Entries[0].Path);
    }

    [Fact]
    public void TreeRowsSayTheKindInWordsAfterTheName()
    {
        var folder = new GitHubTreeEntry(
            "docs", GitHubEntryKind.Folder, "docs",
            "Add the accessible diff viewer",
            new DateTimeOffset(2026, 9, 11, 10, 0, 0, TimeSpan.Zero));

        var file = new GitHubTreeEntry(
            "readme.md", GitHubEntryKind.File, "README.md",
            "Port from React Native Windows to .NET MAUI",
            new DateTimeOffset(2026, 9, 11, 9, 0, 0, TimeSpan.Zero));

        Assert.StartsWith("docs, folder, Add the accessible diff viewer", folder.AccessibleName);
        Assert.StartsWith("readme.md, file, Port from React Native Windows to .NET MAUI", file.AccessibleName);
    }

    [Fact]
    public void FindReadmePrefersMarkdownOverABareReadme()
    {
        var entries = new[]
        {
            new GitHubTreeEntry("README", GitHubEntryKind.File, "README"),
            new GitHubTreeEntry("README.md", GitHubEntryKind.File, "README.md"),
            new GitHubTreeEntry("README", GitHubEntryKind.Folder, "README"),
        };

        Assert.Equal("README.md", GitHubClient.FindReadme(entries)!.Name);
    }

    [Fact]
    public void LastCommitQueryAliasesEachPathAndEscapesQuotes()
    {
        var query = GitHubClient.LastCommitQuery(new[] { "docs", "src/\"odd\"" }, withReadme: true);

        Assert.Contains("p0: history(first: 1, path: \"docs\")", query);
        Assert.Contains("p1: history(first: 1, path: \"src/\\\"odd\\\"\")", query);
        Assert.Contains("$readmeExpression: String!", query);
        Assert.Contains("readme: object(expression: $readmeExpression)", query);
    }

    [Fact]
    public void ApplyLastCommitsFillsTheSubjectWithoutReordering()
    {
        var entries = GitHubClient.SortEntries(new[]
        {
            new GitHubTreeEntry("README.md", GitHubEntryKind.File, "README.md"),
            new GitHubTreeEntry("docs", GitHubEntryKind.Folder, "docs"),
        });

        var filled = GitHubClient.ApplyLastCommits(entries, new Dictionary<string, GitHubClient.LastTouch>
        {
            ["docs"] = new("Add the accessible diff viewer", DateTimeOffset.Parse("2026-09-10T12:00:00Z")),
        });

        Assert.Equal("docs", filled[0].Name);
        Assert.Equal("Add the accessible diff viewer", filled[0].LastSubject);
        Assert.Null(filled[1].LastSubject);
    }

    [Fact]
    public async Task AMissingRepositoryIsASpokenNotFoundNotACrash()
    {
        var handler = new ScriptedHandler(_ => Json(HttpStatusCode.OK, """
            {
              "errors": [
                {
                  "type": "NOT_FOUND",
                  "message": "Could not resolve to a Repository with the name 'ghost/missing'."
                }
              ]
            }
            """));

        using var client = new GitHubClient("t", handler);
        var result = await client.GetRepositoryViewAsync("ghost", "missing");

        Assert.False(result.Success);
        Assert.Equal(GitHubFailure.NotFound, result.Failure);
        Assert.Contains("Could not resolve", result.Error!);
    }

    [Fact]
    public async Task LastTouchFailureStillReturnsTheFiles()
    {
        var handler = new ScriptedHandler(request =>
        {
            var body = request.Content!.ReadAsStringAsync().Result;
            if (body.Contains("RepositoryView", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.OK, Snapshot);
            }

            return Json(HttpStatusCode.OK, """
                {
                  "errors": [ { "message": "Something went wrong." } ]
                }
                """);
        });

        using var client = new GitHubClient("t", handler);
        var result = await client.GetRepositoryViewAsync("G4p-Studios", "GitApp");

        Assert.True(result.Success);
        Assert.Contains(result.Value!.Entries, e => e.Name == "docs");
        Assert.All(result.Value.Entries, e => Assert.Null(e.LastSubject));
    }

    [Fact]
    public async Task LastTouchAndReadmeAreMergedBeforeTheCallerSeesTheList()
    {
        var handler = new ScriptedHandler(request =>
        {
            var body = request.Content!.ReadAsStringAsync().Result;
            if (body.Contains("RepositoryView", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.OK, Snapshot);
            }

            return Json(HttpStatusCode.OK, """
                {
                  "data": {
                    "repository": {
                      "readme": { "text": "# GitApp\n\nHello.", "isBinary": false },
                      "object": {
                        "p0": { "nodes": [ { "messageHeadline": "Add the accessible diff viewer", "committedDate": "2026-09-10T12:00:00Z" } ] },
                        "p1": { "nodes": [ { "messageHeadline": "Port from React Native Windows to .NET MAUI", "committedDate": "2026-09-10T11:00:00Z" } ] },
                        "p2": { "nodes": [ { "messageHeadline": "Milestone 2", "committedDate": "2026-09-10T10:00:00Z" } ] },
                        "p3": { "nodes": [ { "messageHeadline": "Port from React Native Windows to .NET MAUI", "committedDate": "2026-09-10T11:00:00Z" } ] }
                      }
                    }
                  }
                }
                """);
        });

        using var client = new GitHubClient("t", handler);
        var result = await client.GetRepositoryViewAsync("G4p-Studios", "GitApp");

        Assert.True(result.Success);
        Assert.Equal("# GitApp\n\nHello.", result.Value!.ReadmeMarkdown);
        Assert.Equal("README.md", result.Value.ReadmeName);
        Assert.Equal("Add the accessible diff viewer", result.Value.Entries.First(e => e.Name == "docs").LastSubject);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class ScriptedHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

        public ScriptedHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) =>
            _respond = respond;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(_respond(request));
    }
}
