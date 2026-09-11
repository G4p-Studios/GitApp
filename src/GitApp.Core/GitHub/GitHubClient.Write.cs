using System.Text.Json;
using GitApp.Domain;

namespace GitApp.GitHub;

/// <summary>
/// Writes: the mutations behind milestone 4.
///
/// Each one returns what GitHub created, so the screen can show it without
/// a second round trip. Reloading the whole conversation after a post
/// would re-render every comment above the one just written, and a
/// re-render under a screen reader is a screen that went quiet and then
/// started over. See docs/ISSUES.md.
/// </summary>
public sealed partial class GitHubClient
{
    private const string AddCommentMutation = """
        mutation AddComment($subjectId: ID!, $body: String!) {
          addComment(input: {subjectId: $subjectId, body: $body}) {
            commentEdge {
              node {
                author { login }
                createdAt
                body
              }
            }
          }
        }
        """;

    /// <summary>Post a comment on an issue or pull request.</summary>
    /// <param name="subjectId">The GraphQL node id of the issue or pull request.</param>
    public async Task<GitHubResult<GitHubComment>> AddCommentAsync(
        string subjectId, string body, CancellationToken ct = default)
    {
        var result = await GraphqlAsync(
            AddCommentMutation,
            new Dictionary<string, object?>
            {
                ["subjectId"] = subjectId,
                ["body"] = body,
            },
            ct);

        if (!result.Success)
        {
            return GitHubResult<GitHubComment>.Fail(result.Error!, result.Failure);
        }

        try
        {
            var comment = ParseAddedComment(result.Value!);
            return comment is null
                ? GitHubResult<GitHubComment>.Fail(
                    "GitHub did not confirm the comment. Reopen the conversation to check whether it was posted.")
                : GitHubResult<GitHubComment>.Ok(comment);
        }
        catch (JsonException)
        {
            return GitHubResult<GitHubComment>.Fail(
                "GitHub sent a reply GitApp could not read. Reopen the conversation to check whether the comment was posted.");
        }
    }

    /// <summary>The comment GitHub created, out of the mutation reply. Public for tests.</summary>
    public static GitHubComment? ParseAddedComment(string json)
    {
        using var document = JsonDocument.Parse(json);

        return Payload(document.RootElement, "addComment", "commentEdge", "node") is { } node
            ? ReadComment(node)
            : null;
    }

    /// <summary>
    /// Close or reopen an issue or pull request. Four mutations on
    /// GitHub's side, one call on ours; the caller has no reason to care.
    /// </summary>
    public async Task<GitHubResult<GitHubItemState>> SetStateAsync(
        GitHubWorkKind kind, string nodeId, bool open, CancellationToken ct = default)
    {
        var (field, input, subject) = (kind, open) switch
        {
            (GitHubWorkKind.PullRequest, true) => ("reopenPullRequest", "pullRequestId", "pullRequest"),
            (GitHubWorkKind.PullRequest, false) => ("closePullRequest", "pullRequestId", "pullRequest"),
            (_, true) => ("reopenIssue", "issueId", "issue"),
            _ => ("closeIssue", "issueId", "issue"),
        };

        var mutation = $$"""
            mutation SetState($id: ID!) {
              {{field}}(input: {{{input}}: $id}) {
                {{subject}} { state merged }
              }
            }
            """;

        var result = await GraphqlAsync(mutation, new Dictionary<string, object?> { ["id"] = nodeId }, ct);
        if (!result.Success)
        {
            return GitHubResult<GitHubItemState>.Fail(result.Error!, result.Failure);
        }

        try
        {
            var state = ParseState(result.Value!, field, subject);
            return state is { } s
                ? GitHubResult<GitHubItemState>.Ok(s)
                : GitHubResult<GitHubItemState>.Fail(
                    "GitHub did not confirm the change. Reopen the conversation to check.");
        }
        catch (JsonException)
        {
            return GitHubResult<GitHubItemState>.Fail(
                "GitHub sent a reply GitApp could not read. Reopen the conversation to check.");
        }
    }

    /// <summary>The state GitHub reports after a state mutation. Public for tests.</summary>
    public static GitHubItemState? ParseState(string json, string field, string subject)
    {
        using var document = JsonDocument.Parse(json);

        return Payload(document.RootElement, field, subject) is { } node
            ? ReadState(node)
            : null;
    }

    private const string MergeMutation = """
        mutation Merge($id: ID!, $method: PullRequestMergeMethod!) {
          mergePullRequest(input: {pullRequestId: $id, mergeMethod: $method}) {
            pullRequest { state merged }
          }
        }
        """;

    /// <summary>Merge a pull request. GitHub's refusal, if any, is the error text.</summary>
    public async Task<GitHubResult<GitHubItemState>> MergePullRequestAsync(
        string nodeId, MergeMethod method, CancellationToken ct = default)
    {
        var result = await GraphqlAsync(
            MergeMutation,
            new Dictionary<string, object?>
            {
                ["id"] = nodeId,
                ["method"] = method.ApiName(),
            },
            ct);

        if (!result.Success)
        {
            return GitHubResult<GitHubItemState>.Fail(result.Error!, result.Failure);
        }

        try
        {
            var state = ParseState(result.Value!, "mergePullRequest", "pullRequest");
            return state == GitHubItemState.Merged
                ? GitHubResult<GitHubItemState>.Ok(GitHubItemState.Merged)
                : GitHubResult<GitHubItemState>.Fail(
                    "GitHub did not confirm the merge. Reopen the pull request to check.");
        }
        catch (JsonException)
        {
            return GitHubResult<GitHubItemState>.Fail(
                "GitHub sent a reply GitApp could not read. Reopen the pull request to check.");
        }
    }

    private const string ReviewMutation = """
        mutation Review($id: ID!, $event: PullRequestReviewEvent!, $body: String) {
          addPullRequestReview(input: {pullRequestId: $id, event: $event, body: $body}) {
            pullRequestReview {
              author { login }
              createdAt
              body
              state
            }
          }
        }
        """;

    /// <summary>
    /// Submit a review on the whole pull request: approve, request changes,
    /// or comment. Line-by-line review needs the pull request's diff on
    /// screen and is not this.
    /// </summary>
    public async Task<GitHubResult<GitHubComment>> ReviewPullRequestAsync(
        string nodeId, ReviewEvent verdict, string? body, CancellationToken ct = default)
    {
        var result = await GraphqlAsync(
            ReviewMutation,
            new Dictionary<string, object?>
            {
                ["id"] = nodeId,
                ["event"] = verdict.ApiName(),
                ["body"] = string.IsNullOrWhiteSpace(body) ? null : body,
            },
            ct);

        if (!result.Success)
        {
            return GitHubResult<GitHubComment>.Fail(result.Error!, result.Failure);
        }

        try
        {
            var review = ParseReview(result.Value!);
            return review is null
                ? GitHubResult<GitHubComment>.Fail(
                    "GitHub did not confirm the review. Reopen the pull request to check.")
                : GitHubResult<GitHubComment>.Ok(review);
        }
        catch (JsonException)
        {
            return GitHubResult<GitHubComment>.Fail(
                "GitHub sent a reply GitApp could not read. Reopen the pull request to check.");
        }
    }

    /// <summary>The review GitHub created, as a conversation entry. Public for tests.</summary>
    public static GitHubComment? ParseReview(string json)
    {
        using var document = JsonDocument.Parse(json);

        if (Payload(document.RootElement, "addPullRequestReview", "pullRequestReview") is not { } node)
        {
            return null;
        }

        return ReadComment(node) with
        {
            Verdict = ReviewEventWords.VerdictFromState(String(node, "state")) ?? "reviewed",
        };
    }

    /// <summary>Walk data.field.path... and return the object at the end, or null.</summary>
    private static JsonElement? Payload(JsonElement root, params string[] path)
    {
        if (!root.TryGetProperty("data", out var current) || current.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var step in path)
        {
            if (!current.TryGetProperty(step, out current) || current.ValueKind != JsonValueKind.Object)
            {
                return null;
            }
        }

        return current;
    }
}
