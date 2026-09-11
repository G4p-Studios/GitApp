using System.Net;
using System.Text;
using GitApp.Domain;
using GitApp.GitHub;

namespace GitApp.Core.Tests;

public class ReleaseTests
{
    private const string ReleasesJson = """
        {
          "data": {
            "repository": {
              "defaultBranchRef": { "name": "main" },
              "refs": { "nodes": [ { "name": "main" }, { "name": "maui" } ] },
              "releases": {
                "totalCount": 2,
                "nodes": [
                  {
                    "id": "RE_1",
                    "name": "GitApp 0.2",
                    "tagName": "v0.2.0",
                    "isDraft": false,
                    "isPrerelease": false,
                    "isLatest": true,
                    "createdAt": "2026-09-10T12:00:00Z",
                    "publishedAt": "2026-09-10T13:00:00Z",
                    "url": "https://github.com/G4p-Studios/GitApp/releases/tag/v0.2.0",
                    "description": "## Highlights\n\nFolding.",
                    "author": { "login": "alexoloopios" },
                    "releaseAssets": {
                      "totalCount": 2,
                      "nodes": [
                        { "name": "GitApp-x64.msix", "size": 18874368, "downloadUrl": "https://example.com/a" },
                        { "name": "checksums.txt", "size": 512, "downloadUrl": "https://example.com/b" }
                      ]
                    }
                  },
                  {
                    "id": "RE_2",
                    "name": null,
                    "tagName": "v0.1.0-beta",
                    "isDraft": true,
                    "isPrerelease": true,
                    "isLatest": false,
                    "createdAt": "2026-09-01T12:00:00Z",
                    "publishedAt": null,
                    "url": "https://github.com/G4p-Studios/GitApp/releases/tag/v0.1.0-beta",
                    "description": null,
                    "author": null,
                    "releaseAssets": { "totalCount": 0, "nodes": [] }
                  }
                ]
              }
            }
          }
        }
        """;

    [Fact]
    public void RowsLeadWithTheTitleAndSayTheTagOnlyWhenItAddsSomething()
    {
        var list = GitHubClient.ParseReleases(ReleasesJson)!;

        Assert.StartsWith("GitApp 0.2, tag v0.2.0, latest, by alexoloopios, 2 assets", list.Items[0].AccessibleName);

        var unnamed = list.Items[1];
        Assert.Equal("v0.1.0-beta", unnamed.Title);
        Assert.StartsWith("v0.1.0-beta, draft, pre-release", unnamed.AccessibleName);
        Assert.DoesNotContain("tag v0.1.0-beta", unnamed.AccessibleName);
        Assert.DoesNotContain("by ", unnamed.AccessibleName);
        Assert.DoesNotContain("asset", unnamed.AccessibleName);
    }

    [Fact]
    public void TheListCarriesBranchesForTheNewReleaseForm()
    {
        var list = GitHubClient.ParseReleases(ReleasesJson)!;

        Assert.Equal("main", list.DefaultBranch);
        Assert.Equal(new[] { "main", "maui" }, list.Branches);
        Assert.Equal("2 releases", list.Summary);
        Assert.False(list.Truncated);
    }

    [Fact]
    public void AssetsSayTheirSizeInWords()
    {
        var release = GitHubClient.ParseReleases(ReleasesJson)!.Items[0];

        Assert.Equal("2 assets", release.AssetsHeading);
        Assert.Equal("GitApp-x64.msix, 19 megabytes", release.Assets[0].AccessibleName);
        Assert.Equal("checksums.txt, 512 bytes", release.Assets[1].AccessibleName);
    }

    [Fact]
    public void ADraftSaysCreatedNotPublished()
    {
        var list = GitHubClient.ParseReleases(ReleasesJson)!;

        Assert.StartsWith("release, latest, by alexoloopios, published", list.Items[0].Metadata);
        Assert.StartsWith("draft, created", list.Items[1].Metadata);
        Assert.Equal("No assets", list.Items[1].AssetsHeading);
    }

    [Fact]
    public void AnEmptyListIsSaidNotShownAsNothing()
    {
        var json = """{ "data": { "repository": { "defaultBranchRef": null, "refs": { "nodes": [] }, "releases": { "totalCount": 0, "nodes": [] } } } }""";

        Assert.Equal("No releases yet", GitHubClient.ParseReleases(json)!.Summary);
    }

    [Theory]
    [InlineData("", "A tag is required")]
    [InlineData("   ", "A tag is required")]
    [InlineData("v1 .0", "cannot contain spaces")]
    [InlineData("v1..0", "not a valid tag")]
    [InlineData("-v1", "not a valid tag")]
    public void ABadTagIsExplainedBeforeItReachesGitHub(string tag, string expected)
    {
        var release = new NewRelease(tag, "main", null, null, false, false);

        Assert.Contains(expected, release.Problem);
    }

    [Fact]
    public void AGoodTagHasNoProblemAndTheActionWordFollowsDraft()
    {
        Assert.Null(new NewRelease("v1.0.0", "main", "One", "Notes", false, false).Problem);
        Assert.Equal("Publish release", new NewRelease("v1", null, null, null, false, false).ActionWord);
        Assert.Equal("Save draft", new NewRelease("v1", null, null, null, false, true).ActionWord);
    }

    [Fact]
    public async Task CreatingARelease_PostsRestAndReturnsTheRelease()
    {
        HttpRequestMessage? sent = null;
        string? body = null;
        var handler = new ScriptedHandler(request =>
        {
            sent = request;
            body = request.Content!.ReadAsStringAsync().Result;
            return Json(HttpStatusCode.Created, """
                {
                  "node_id": "RE_3",
                  "tag_name": "v0.3.0",
                  "name": "GitApp 0.3",
                  "draft": false,
                  "prerelease": true,
                  "created_at": "2026-09-11T18:00:00Z",
                  "published_at": "2026-09-11T18:00:00Z",
                  "html_url": "https://github.com/G4p-Studios/GitApp/releases/tag/v0.3.0",
                  "body": "Milestone 4.",
                  "author": { "login": "alexoloopios" }
                }
                """);
        });

        using var client = new GitHubClient("t", handler);
        var result = await client.CreateReleaseAsync(
            "G4p-Studios", "GitApp",
            new NewRelease("v0.3.0", "main", "GitApp 0.3", "Milestone 4.", IsPrerelease: true, IsDraft: false));

        Assert.True(result.Success);
        Assert.Equal(HttpMethod.Post, sent!.Method);
        Assert.EndsWith("/repos/G4p-Studios/GitApp/releases", sent.RequestUri!.ToString());
        Assert.Contains("\"tag_name\":\"v0.3.0\"", body);
        Assert.Contains("\"target_commitish\":\"main\"", body);
        Assert.Contains("\"prerelease\":true", body);
        Assert.Equal("GitApp 0.3", result.Value!.Title);
        Assert.True(result.Value.IsPrerelease);
        Assert.StartsWith("GitApp 0.3, tag v0.3.0, pre-release, by alexoloopios", result.Value.AccessibleName);
    }

    [Fact]
    public async Task ABlankTitleAndNotesAreOmittedNotSentAsEmptyStrings()
    {
        string? body = null;
        var handler = new ScriptedHandler(request =>
        {
            body = request.Content!.ReadAsStringAsync().Result;
            return Json(HttpStatusCode.Created, """{ "tag_name": "v1", "draft": true, "created_at": "2026-09-11T18:00:00Z" }""");
        });

        using var client = new GitHubClient("t", handler);
        var result = await client.CreateReleaseAsync("o", "r", new NewRelease("v1", null, "  ", "", false, true));

        Assert.True(result.Success);
        Assert.DoesNotContain("\"name\"", body);
        Assert.DoesNotContain("\"body\"", body);
        Assert.DoesNotContain("target_commitish", body);
        Assert.True(result.Value!.IsDraft);
        Assert.Equal("v1", result.Value.Title);
    }

    [Fact]
    public async Task AnInvalidTagNeverLeavesTheApp()
    {
        var handler = new ScriptedHandler(_ => throw new InvalidOperationException("should not be called"));

        using var client = new GitHubClient("t", handler);
        var result = await client.CreateReleaseAsync("o", "r", new NewRelease("", null, null, null, false, false));

        Assert.False(result.Success);
        Assert.Contains("tag is required", result.Error);
    }

    [Fact]
    public async Task ADuplicateTagIsExplainedInWords()
    {
        var handler = new ScriptedHandler(_ => Json(HttpStatusCode.UnprocessableEntity, """
            { "message": "Validation Failed", "errors": [ { "resource": "Release", "code": "already_exists", "field": "tag_name" } ] }
            """));

        using var client = new GitHubClient("t", handler);
        var result = await client.CreateReleaseAsync("o", "r", new NewRelease("v1", null, null, null, false, false));

        Assert.False(result.Success);
        Assert.Contains("may already exist", result.Error);
        Assert.DoesNotContain("422", result.Error);
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
