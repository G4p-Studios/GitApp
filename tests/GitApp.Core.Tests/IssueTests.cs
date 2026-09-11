using System.Net;
using System.Text;
using GitApp.Domain;
using GitApp.GitHub;

namespace GitApp.Core.Tests;

public class IssueTests
{
    private const string IssuePage = """
        {
          "data": {
            "repository": {
              "issues": {
                "totalCount": 2,
                "pageInfo": { "hasNextPage": false, "endCursor": "Y" },
                "nodes": [
                  {
                    "number": 17,
                    "title": "Fold large differences",
                    "state": "OPEN",
                    "author": { "login": "alexoloopios" },
                    "updatedAt": "2026-09-11T12:00:00Z",
                    "url": "https://github.com/G4p-Studios/GitApp/issues/17",
                    "comments": { "totalCount": 3 },
                    "labels": { "nodes": [ { "name": "accessibility" }, { "name": "diff" } ] }
                  },
                  {
                    "number": 4,
                    "title": "Sign in with a token",
                    "state": "CLOSED",
                    "author": null,
                    "updatedAt": "2026-09-10T12:00:00Z",
                    "url": "https://github.com/G4p-Studios/GitApp/issues/4",
                    "comments": { "totalCount": 0 },
                    "labels": { "nodes": [] }
                  }
                ]
              }
            }
          }
        }
        """;

    private const string PullPage = """
        {
          "data": {
            "repository": {
              "pullRequests": {
                "totalCount": 1,
                "pageInfo": { "hasNextPage": false, "endCursor": null },
                "nodes": [
                  {
                    "number": 12,
                    "title": "Port to MAUI",
                    "state": "OPEN",
                    "isDraft": true,
                    "author": { "login": "alexoloopios" },
                    "updatedAt": "2026-09-11T08:00:00Z",
                    "url": "https://github.com/G4p-Studios/GitApp/pull/12",
                    "comments": { "totalCount": 2 },
                    "labels": { "nodes": [ { "name": "work-in-progress" } ] },
                    "headRefName": "maui",
                    "baseRefName": "main"
                  }
                ]
              }
            }
          }
        }
        """;

    private const string IssueDetail = """
        {
          "data": {
            "repository": {
              "issue": {
                "number": 17,
                "title": "Fold large differences",
                "state": "OPEN",
                "body": "Large hunks should start folded.\n\nSee [the spec](https://example.com).",
                "author": { "login": "alexoloopios" },
                "createdAt": "2026-09-10T12:00:00Z",
                "updatedAt": "2026-09-11T12:00:00Z",
                "url": "https://github.com/G4p-Studios/GitApp/issues/17",
                "comments": {
                  "totalCount": 1,
                  "nodes": [
                    {
                      "author": { "login": "claude" },
                      "createdAt": "2026-09-11T10:00:00Z",
                      "body": "Agreed."
                    }
                  ]
                },
                "labels": { "nodes": [ { "name": "accessibility" } ] },
                "assignees": { "nodes": [ { "login": "alexoloopios" } ] },
                "milestone": { "title": "Milestone 3" }
              }
            }
          }
        }
        """;

    [Fact]
    public void IssueRowsLeadWithTheTitleAndSayTheKindLast()
    {
        var list = GitHubClient.ParseIssueList(IssuePage);
        var open = list!.Items[0];

        Assert.StartsWith("Fold large differences, open", open.AccessibleName);
        Assert.Contains("accessibility", open.AccessibleName);
        Assert.Contains("by alexoloopios", open.AccessibleName);
        Assert.Contains("3 comments", open.AccessibleName);
        Assert.EndsWith("issue 17", open.AccessibleName);
        Assert.DoesNotContain("Issue 17, Fold", open.AccessibleName);
    }

    [Fact]
    public void AClosedIssueWithoutAnAuthorDoesNotInventAName()
    {
        var closed = GitHubClient.ParseIssueList(IssuePage)!.Items[1];

        Assert.Contains("closed", closed.AccessibleName);
        Assert.DoesNotContain("by ", closed.AccessibleName);
        Assert.DoesNotContain("comment", closed.AccessibleName);
        Assert.EndsWith("issue 4", closed.AccessibleName);
    }

    [Fact]
    public void PullRequestRowsNameTheBranchesAfterTheTitle()
    {
        var pr = Assert.Single(GitHubClient.ParsePullRequestList(PullPage)!.Items);

        Assert.StartsWith("Port to MAUI, open, draft, maui into main", pr.AccessibleName);
        Assert.Contains("work-in-progress", pr.AccessibleName);
        Assert.EndsWith("pull request 12", pr.AccessibleName);
    }

    [Fact]
    public void IssueDetailKeepsTheOpeningPostSeparateFromComments()
    {
        var detail = GitHubClient.ParseIssueDetail(IssueDetail);

        Assert.NotNull(detail);
        Assert.Contains("Large hunks should start folded", detail!.BodyMarkdown);
        Assert.Equal("1 comment", detail.CommentsHeading);
        var comment = Assert.Single(detail.Comments);
        Assert.Equal("claude", comment.Author);
        Assert.StartsWith("claude,", comment.Heading);
        Assert.Contains("Assigned to alexoloopios", detail.AboutFacts);
        Assert.Contains("Milestone, Milestone 3", detail.AboutFacts);
        Assert.Contains("Labels, accessibility", detail.AboutFacts);
    }

    [Fact]
    public void AMissingIssueIsNullRatherThanAnEmptyConversation()
    {
        var json = """{ "data": { "repository": { "issue": null } } }""";

        Assert.Null(GitHubClient.ParseIssueDetail(json));
    }

    [Fact]
    public async Task AMissingRepositoryOnTheIssueListIsNotFound()
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
        var result = await client.GetIssuesAsync("ghost", "missing");

        Assert.False(result.Success);
        Assert.Equal(GitHubFailure.NotFound, result.Failure);
    }

    [Fact]
    public async Task ASecondPageIsFollowedAndAShortPageStops()
    {
        var fullNodes = string.Join(",", Enumerable.Range(1, 50).Select(i =>
            $$"""{ "number": {{i}}, "title": "Issue {{i}}", "state": "OPEN", "author": { "login": "a" }, "updatedAt": "2026-09-11T00:00:00Z", "url": "https://example.com/{{i}}", "comments": { "totalCount": 0 }, "labels": { "nodes": [] } }"""));

        var handler = new ScriptedHandler(request =>
        {
            var body = request.Content!.ReadAsStringAsync().Result;
            if (body.Contains("\"after\":null", StringComparison.Ordinal)
                || body.Contains("\"after\": null", StringComparison.Ordinal)
                || !body.Contains("after", StringComparison.Ordinal))
            {
                // First page: full, has next.
            }

            if (!body.Contains("cursor-1", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.OK, $$"""
                    { "data": { "repository": { "issues": {
                      "totalCount": 51,
                      "pageInfo": { "hasNextPage": true, "endCursor": "cursor-1" },
                      "nodes": [ {{fullNodes}} ]
                    } } } }
                    """);
            }

            return Json(HttpStatusCode.OK, """
                { "data": { "repository": { "issues": {
                  "totalCount": 51,
                  "pageInfo": { "hasNextPage": false, "endCursor": "cursor-2" },
                  "nodes": [
                    { "number": 51, "title": "Last", "state": "OPEN", "author": { "login": "a" }, "updatedAt": "2026-09-11T00:00:00Z", "url": "https://example.com/51", "comments": { "totalCount": 0 }, "labels": { "nodes": [] } }
                  ]
                } } } }
                """);
        });

        using var client = new GitHubClient("t", handler);
        var result = await client.GetIssuesAsync("G4p-Studios", "GitApp");

        Assert.True(result.Success);
        Assert.Equal(51, result.Value!.Items.Count);
        Assert.False(result.Value.Truncated);
        Assert.Equal("Last", result.Value.Items[^1].Title);
    }

    [Fact]
    public void TruncationIsReportedRatherThanSilent()
    {
        var json = """
            { "data": { "repository": { "issues": {
              "totalCount": 400,
              "pageInfo": { "hasNextPage": true, "endCursor": "x" },
              "nodes": [
                { "number": 1, "title": "One", "state": "OPEN", "author": { "login": "a" }, "updatedAt": "2026-09-11T00:00:00Z", "url": "https://example.com/1", "comments": { "totalCount": 0 }, "labels": { "nodes": [] } }
              ]
            } } } }
            """;

        var list = GitHubClient.ParseIssueList(json);
        Assert.True(list!.Truncated);
        Assert.Equal(400, list.TotalCount);
        Assert.Single(list.Items);
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
