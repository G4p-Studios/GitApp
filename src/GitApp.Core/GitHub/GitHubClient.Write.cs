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

        if (!document.RootElement.TryGetProperty("data", out var data)
            || data.ValueKind != JsonValueKind.Object
            || !data.TryGetProperty("addComment", out var payload)
            || payload.ValueKind != JsonValueKind.Object
            || !payload.TryGetProperty("commentEdge", out var edge)
            || edge.ValueKind != JsonValueKind.Object
            || !edge.TryGetProperty("node", out var node)
            || node.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return ReadComment(node);
    }
}
