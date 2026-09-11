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
                "id": "I_kwDOAbc123",
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

    [Fact]
    public void TheDetailCarriesTheNodeIdACommentNeeds()
    {
        var detail = GitHubClient.ParseIssueDetail(IssueDetail)!;

        Assert.Equal("I_kwDOAbc123", detail.NodeId);
        Assert.True(detail.CanComment);
    }

    [Fact]
    public void ADetailWithoutANodeIdCannotBeCommentedOn()
    {
        var json = """{ "data": { "repository": { "issue": { "number": 1, "title": "T", "state": "OPEN" } } } }""";

        Assert.False(GitHubClient.ParseIssueDetail(json)!.CanComment);
    }

    [Fact]
    public void APostedCommentIsAppendedAndTheHeadingCountsIt()
    {
        var detail = GitHubClient.ParseIssueDetail(IssueDetail)!;

        var after = detail.WithComment(new GitHubComment("alexoloopios", DateTimeOffset.UtcNow, "Done."));

        Assert.Equal(2, after.Comments.Count);
        Assert.Equal("Done.", after.Comments[^1].BodyMarkdown);
        Assert.Equal("2 comments", after.CommentsHeading);
        Assert.Equal(2, after.Item.CommentCount);
        Assert.Single(detail.Comments);
    }

    [Fact]
    public void TheMutationReplyBecomesAComment()
    {
        var json = """
            { "data": { "addComment": { "commentEdge": { "node": {
              "author": { "login": "alexoloopios" },
              "createdAt": "2026-09-11T17:00:00Z",
              "body": "Fixed in 1aabfb0."
            } } } } }
            """;

        var comment = GitHubClient.ParseAddedComment(json);

        Assert.NotNull(comment);
        Assert.Equal("alexoloopios", comment!.Author);
        Assert.Equal("Fixed in 1aabfb0.", comment.BodyMarkdown);
        Assert.StartsWith("alexoloopios,", comment.Heading);
    }

    [Fact]
    public void AReplyWithoutTheCommentIsNullNotAnEmptyComment()
    {
        Assert.Null(GitHubClient.ParseAddedComment("""{ "data": { "addComment": null } }"""));
    }

    [Fact]
    public async Task AddCommentSendsTheSubjectAndBodyAndReturnsTheComment()
    {
        string? sent = null;
        var handler = new ScriptedHandler(request =>
        {
            sent = request.Content!.ReadAsStringAsync().Result;
            return Json(HttpStatusCode.OK, """
                { "data": { "addComment": { "commentEdge": { "node": {
                  "author": { "login": "alexoloopios" },
                  "createdAt": "2026-09-11T17:00:00Z",
                  "body": "Looks right."
                } } } } }
                """);
        });

        using var client = new GitHubClient("t", handler);
        var result = await client.AddCommentAsync("I_kwDOAbc123", "Looks right.");

        Assert.True(result.Success);
        Assert.Equal("Looks right.", result.Value!.BodyMarkdown);
        Assert.Contains("addComment", sent);
        Assert.Contains("\"subjectId\":\"I_kwDOAbc123\"", sent);
        Assert.Contains("\"body\":\"Looks right.\"", sent);
    }

    [Fact]
    public async Task ARefusedCommentIsASpokenFailureWithTheScopeHint()
    {
        var handler = new ScriptedHandler(_ => Json(HttpStatusCode.OK, """
            {
              "data": { "addComment": null },
              "errors": [
                { "type": "FORBIDDEN", "message": "Resource not accessible by personal access token" }
              ]
            }
            """));

        using var client = new GitHubClient("t", handler);
        var result = await client.AddCommentAsync("I_kwDOAbc123", "Hello");

        Assert.False(result.Success);
        Assert.Equal(GitHubFailure.Forbidden, result.Failure);
        Assert.Contains("not accessible", result.Error);
    }

    [Fact]
    public async Task AnUnconfirmedCommentSaysToCheckRatherThanClaimingSuccess()
    {
        var handler = new ScriptedHandler(_ => Json(HttpStatusCode.OK, """{ "data": { "addComment": {} } }"""));

        using var client = new GitHubClient("t", handler);
        var result = await client.AddCommentAsync("I_kwDOAbc123", "Hello");

        Assert.False(result.Success);
        Assert.Contains("check whether it was posted", result.Error);
    }

    private const string PullDetail = """
        {
          "data": {
            "repository": {
              "mergeCommitAllowed": true,
              "squashMergeAllowed": true,
              "rebaseMergeAllowed": false,
              "pullRequest": {
                "id": "PR_kwDOAbc456",
                "number": 12,
                "title": "Port to MAUI",
                "state": "OPEN",
                "isDraft": false,
                "body": "Ports the shell.",
                "author": { "login": "alexoloopios" },
                "createdAt": "2026-09-10T12:00:00Z",
                "updatedAt": "2026-09-11T12:00:00Z",
                "url": "https://github.com/G4p-Studios/GitApp/pull/12",
                "headRefName": "maui",
                "baseRefName": "main",
                "merged": false,
                "mergeable": "MERGEABLE",
                "reviewDecision": "APPROVED",
                "additions": 10,
                "deletions": 2,
                "changedFiles": 3,
                "commits": { "totalCount": 4 },
                "comments": {
                  "totalCount": 1,
                  "nodes": [
                    { "author": { "login": "claude" }, "createdAt": "2026-09-11T09:00:00Z", "body": "Second." }
                  ]
                },
                "reviews": {
                  "nodes": [
                    { "author": { "login": "reviewer" }, "createdAt": "2026-09-11T08:00:00Z", "body": "", "state": "APPROVED" },
                    { "author": { "login": "reviewer" }, "createdAt": "2026-09-11T07:00:00Z", "body": "", "state": "COMMENTED" },
                    { "author": { "login": "other" }, "createdAt": "2026-09-11T10:00:00Z", "body": "Please rename.", "state": "CHANGES_REQUESTED" }
                  ]
                },
                "labels": { "nodes": [] },
                "assignees": { "nodes": [] },
                "milestone": null
              }
            }
          }
        }
        """;

    [Fact]
    public void ReviewsJoinTheConversationInDateOrderWithTheirVerdictInTheHeading()
    {
        var detail = GitHubClient.ParsePullRequestDetail(PullDetail)!;

        // The wordless COMMENTED review is a shell around line comments and is left out.
        Assert.Equal(3, detail.Comments.Count);
        Assert.StartsWith("reviewer approved,", detail.Comments[0].Heading);
        Assert.StartsWith("claude,", detail.Comments[1].Heading);
        Assert.StartsWith("other requested changes,", detail.Comments[2].Heading);
        Assert.Equal("Please rename.", detail.Comments[2].BodyMarkdown);
        Assert.True(detail.Comments[0].IsReview);
        Assert.False(detail.Comments[1].IsReview);
    }

    [Fact]
    public void ThePullRequestKnowsWhichMergeMethodsTheRepositoryAllows()
    {
        var detail = GitHubClient.ParsePullRequestDetail(PullDetail)!;

        Assert.Equal(new[] { MergeMethod.Merge, MergeMethod.Squash }, detail.MergeMethods);
        Assert.True(detail.CanMerge);
        Assert.True(detail.IsOpenPullRequest);
        Assert.Equal("Close pull request", detail.StateActionLabel);
        Assert.Contains("Review approved", detail.AboutFacts);
    }

    [Fact]
    public void ADraftOrConflictingOrClosedPullRequestOffersNoMerge()
    {
        var detail = GitHubClient.ParsePullRequestDetail(PullDetail)!;

        Assert.False((detail with { Item = detail.Item with { IsDraft = true } }).CanMerge);
        Assert.False((detail with { Mergeable = "CONFLICTING" }).CanMerge);
        Assert.False(detail.WithState(GitHubItemState.Closed).CanMerge);
        Assert.False((detail with { MergeMethods = Array.Empty<MergeMethod>() }).CanMerge);
    }

    [Fact]
    public void ChangingStateChangesTheLabelAndMergedIsFinal()
    {
        var detail = GitHubClient.ParseIssueDetail(IssueDetail)!;

        var closed = detail.WithState(GitHubItemState.Closed);
        Assert.Equal("Reopen issue", closed.StateActionLabel);
        Assert.Equal("closed", closed.Metadata.Split(", ")[0]);
        Assert.True(closed.CanChangeState);

        var pr = GitHubClient.ParsePullRequestDetail(PullDetail)!.WithState(GitHubItemState.Merged);
        Assert.False(pr.CanChangeState);
        Assert.Contains("merged", pr.AboutFacts);
    }

    [Fact]
    public async Task ClosingAnIssueUsesTheIssueMutationAndReturnsTheNewState()
    {
        string? sent = null;
        var handler = new ScriptedHandler(request =>
        {
            sent = request.Content!.ReadAsStringAsync().Result;
            return Json(HttpStatusCode.OK, """{ "data": { "closeIssue": { "issue": { "state": "CLOSED", "merged": false } } } }""");
        });

        using var client = new GitHubClient("t", handler);
        var result = await client.SetStateAsync(GitHubWorkKind.Issue, "I_1", open: false);

        Assert.True(result.Success);
        Assert.Equal(GitHubItemState.Closed, result.Value);
        Assert.Contains("closeIssue(input: {issueId: $id})", sent);
    }

    [Fact]
    public async Task ReopeningAPullRequestUsesThePullRequestMutation()
    {
        string? sent = null;
        var handler = new ScriptedHandler(request =>
        {
            sent = request.Content!.ReadAsStringAsync().Result;
            return Json(HttpStatusCode.OK, """{ "data": { "reopenPullRequest": { "pullRequest": { "state": "OPEN", "merged": false } } } }""");
        });

        using var client = new GitHubClient("t", handler);
        var result = await client.SetStateAsync(GitHubWorkKind.PullRequest, "PR_1", open: true);

        Assert.Equal(GitHubItemState.Open, result.Value);
        Assert.Contains("reopenPullRequest(input: {pullRequestId: $id})", sent);
    }

    [Fact]
    public async Task MergeSendsTheMethodAndOnlyMergedCountsAsSuccess()
    {
        string? sent = null;
        var handler = new ScriptedHandler(request =>
        {
            sent = request.Content!.ReadAsStringAsync().Result;
            return Json(HttpStatusCode.OK, """{ "data": { "mergePullRequest": { "pullRequest": { "state": "MERGED", "merged": true } } } }""");
        });

        using var client = new GitHubClient("t", handler);
        var result = await client.MergePullRequestAsync("PR_1", MergeMethod.Squash);

        Assert.True(result.Success);
        Assert.Equal(GitHubItemState.Merged, result.Value);
        Assert.Contains("\"method\":\"SQUASH\"", sent);

        var unconfirmed = new ScriptedHandler(_ => Json(HttpStatusCode.OK,
            """{ "data": { "mergePullRequest": { "pullRequest": { "state": "OPEN", "merged": false } } } }"""));
        using var client2 = new GitHubClient("t", unconfirmed);
        var second = await client2.MergePullRequestAsync("PR_1", MergeMethod.Merge);

        Assert.False(second.Success);
        Assert.Contains("did not confirm the merge", second.Error);
    }

    [Fact]
    public async Task ABlockedMergeSpeaksGitHubsReason()
    {
        var handler = new ScriptedHandler(_ => Json(HttpStatusCode.OK, """
            {
              "data": { "mergePullRequest": null },
              "errors": [ { "type": "UNPROCESSABLE", "message": "Pull Request is not mergeable: required status check \"build\" is failing" } ]
            }
            """));

        using var client = new GitHubClient("t", handler);
        var result = await client.MergePullRequestAsync("PR_1", MergeMethod.Merge);

        Assert.False(result.Success);
        Assert.Contains("required status check", result.Error);
    }

    [Fact]
    public async Task AReviewComesBackAsAConversationEntryWithItsVerdict()
    {
        string? sent = null;
        var handler = new ScriptedHandler(request =>
        {
            sent = request.Content!.ReadAsStringAsync().Result;
            return Json(HttpStatusCode.OK, """
                { "data": { "addPullRequestReview": { "pullRequestReview": {
                  "author": { "login": "alexoloopios" },
                  "createdAt": "2026-09-11T17:00:00Z",
                  "body": "Rename the pane.",
                  "state": "CHANGES_REQUESTED"
                } } } }
                """);
        });

        using var client = new GitHubClient("t", handler);
        var result = await client.ReviewPullRequestAsync("PR_1", ReviewEvent.RequestChanges, "Rename the pane.");

        Assert.True(result.Success);
        Assert.Equal("requested changes", result.Value!.Verdict);
        Assert.StartsWith("alexoloopios requested changes,", result.Value.Heading);
        Assert.Contains("\"event\":\"REQUEST_CHANGES\"", sent);
    }

    [Fact]
    public async Task AnApprovalWithNoWordsSendsNoBody()
    {
        string? sent = null;
        var handler = new ScriptedHandler(request =>
        {
            sent = request.Content!.ReadAsStringAsync().Result;
            return Json(HttpStatusCode.OK, """
                { "data": { "addPullRequestReview": { "pullRequestReview": {
                  "author": { "login": "alexoloopios" }, "createdAt": "2026-09-11T17:00:00Z", "body": "", "state": "APPROVED"
                } } } }
                """);
        });

        using var client = new GitHubClient("t", handler);
        var result = await client.ReviewPullRequestAsync("PR_1", ReviewEvent.Approve, "   ");

        Assert.Equal("approved", result.Value!.Verdict);
        Assert.Contains("\"body\":null", sent);
    }

    [Fact]
    public void MergeMethodLabelsAreGitHubsButtonWords()
    {
        Assert.Equal("Create a merge commit", MergeMethod.Merge.Label());
        Assert.Equal("Squash and merge", MergeMethod.Squash.Label());
        Assert.Equal("Rebase and merge", MergeMethod.Rebase.Label());
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
