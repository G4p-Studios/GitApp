using System.Text.Json;
using GitApp.Domain;

namespace GitApp.GitHub;

/// <summary>
/// Issues and pull requests, over GraphQL.
///
/// One request per page of the list, paged up to a cap rather than to
/// exhaustion: a repository can have thousands of closed issues, and
/// loading them all would look like a hang. The summary says when more
/// exist. Filter is local, the same as the repository list.
/// See docs/ISSUES.md.
/// </summary>
public sealed partial class GitHubClient
{
    private const int WorkMaxPages = 4;

    private const string IssueListQuery = """
        query IssueList($owner: String!, $name: String!, $states: [IssueState!], $after: String) {
          repository(owner: $owner, name: $name) {
            issues(first: 50, states: $states, orderBy: {field: UPDATED_AT, direction: DESC}, after: $after) {
              totalCount
              pageInfo { hasNextPage endCursor }
              nodes {
                number
                title
                state
                author { login }
                updatedAt
                url
                comments { totalCount }
                labels(first: 8) { nodes { name } }
              }
            }
          }
        }
        """;

    private const string PullListQuery = """
        query PullList($owner: String!, $name: String!, $states: [PullRequestState!], $after: String) {
          repository(owner: $owner, name: $name) {
            pullRequests(first: 50, states: $states, orderBy: {field: UPDATED_AT, direction: DESC}, after: $after) {
              totalCount
              pageInfo { hasNextPage endCursor }
              nodes {
                number
                title
                state
                isDraft
                author { login }
                updatedAt
                url
                comments { totalCount }
                labels(first: 8) { nodes { name } }
                headRefName
                baseRefName
              }
            }
          }
        }
        """;

    private const string IssueDetailQuery = """
        query IssueDetail($owner: String!, $name: String!, $number: Int!) {
          repository(owner: $owner, name: $name) {
            issue(number: $number) {
              number
              title
              state
              body
              author { login }
              createdAt
              updatedAt
              url
              comments(first: 80) {
                totalCount
                nodes {
                  author { login }
                  createdAt
                  body
                }
              }
              labels(first: 20) { nodes { name } }
              assignees(first: 10) { nodes { login } }
              milestone { title }
            }
          }
        }
        """;

    private const string PullDetailQuery = """
        query PullDetail($owner: String!, $name: String!, $number: Int!) {
          repository(owner: $owner, name: $name) {
            pullRequest(number: $number) {
              number
              title
              state
              isDraft
              body
              author { login }
              createdAt
              updatedAt
              url
              headRefName
              baseRefName
              merged
              mergeable
              additions
              deletions
              changedFiles
              commits { totalCount }
              comments(first: 80) {
                totalCount
                nodes {
                  author { login }
                  createdAt
                  body
                }
              }
              labels(first: 20) { nodes { name } }
              assignees(first: 10) { nodes { login } }
              milestone { title }
            }
          }
        }
        """;

    public Task<GitHubResult<GitHubWorkList>> GetIssuesAsync(
        string owner,
        string name,
        GitHubItemState? state = GitHubItemState.Open,
        CancellationToken ct = default) =>
        GetWorkListAsync(owner, name, GitHubWorkKind.Issue, state, ct);

    public Task<GitHubResult<GitHubWorkList>> GetPullRequestsAsync(
        string owner,
        string name,
        GitHubItemState? state = GitHubItemState.Open,
        CancellationToken ct = default) =>
        GetWorkListAsync(owner, name, GitHubWorkKind.PullRequest, state, ct);

    public async Task<GitHubResult<GitHubWorkDetail>> GetIssueAsync(
        string owner, string name, int number, CancellationToken ct = default)
    {
        var result = await GraphqlAsync(
            IssueDetailQuery,
            new Dictionary<string, object?>
            {
                ["owner"] = owner,
                ["name"] = name,
                ["number"] = number,
            },
            ct);

        if (!result.Success)
        {
            return GitHubResult<GitHubWorkDetail>.Fail(result.Error!, result.Failure);
        }

        try
        {
            var detail = ParseIssueDetail(result.Value!);
            return detail is null
                ? GitHubResult<GitHubWorkDetail>.Fail(
                    "GitHub could not find that issue. It may be a pull request, or it may have been deleted.",
                    GitHubFailure.NotFound)
                : GitHubResult<GitHubWorkDetail>.Ok(detail);
        }
        catch (JsonException)
        {
            return GitHubResult<GitHubWorkDetail>.Fail("GitHub sent a reply GitApp could not read.");
        }
    }

    public async Task<GitHubResult<GitHubWorkDetail>> GetPullRequestAsync(
        string owner, string name, int number, CancellationToken ct = default)
    {
        var result = await GraphqlAsync(
            PullDetailQuery,
            new Dictionary<string, object?>
            {
                ["owner"] = owner,
                ["name"] = name,
                ["number"] = number,
            },
            ct);

        if (!result.Success)
        {
            return GitHubResult<GitHubWorkDetail>.Fail(result.Error!, result.Failure);
        }

        try
        {
            var detail = ParsePullRequestDetail(result.Value!);
            return detail is null
                ? GitHubResult<GitHubWorkDetail>.Fail(
                    "GitHub could not find that pull request. It may have been deleted.",
                    GitHubFailure.NotFound)
                : GitHubResult<GitHubWorkDetail>.Ok(detail);
        }
        catch (JsonException)
        {
            return GitHubResult<GitHubWorkDetail>.Fail("GitHub sent a reply GitApp could not read.");
        }
    }

    /// <summary>Turn a GraphQL issue list page into ours. Public for tests.</summary>
    public static GitHubWorkList? ParseIssueList(string json) =>
        ParseWorkList(json, "issues", GitHubWorkKind.Issue);

    /// <summary>Turn a GraphQL pull request list page into ours. Public for tests.</summary>
    public static GitHubWorkList? ParsePullRequestList(string json) =>
        ParseWorkList(json, "pullRequests", GitHubWorkKind.PullRequest);

    public static GitHubWorkDetail? ParseIssueDetail(string json) =>
        ParseWorkDetail(json, "issue", GitHubWorkKind.Issue);

    public static GitHubWorkDetail? ParsePullRequestDetail(string json) =>
        ParseWorkDetail(json, "pullRequest", GitHubWorkKind.PullRequest);

    private async Task<GitHubResult<GitHubWorkList>> GetWorkListAsync(
        string owner,
        string name,
        GitHubWorkKind kind,
        GitHubItemState? state,
        CancellationToken ct)
    {
        var query = kind == GitHubWorkKind.PullRequest ? PullListQuery : IssueListQuery;
        var field = kind == GitHubWorkKind.PullRequest ? "pullRequests" : "issues";
        var items = new List<GitHubWorkItem>();
        string? after = null;
        var total = 0;
        var truncated = false;

        for (var page = 0; page < WorkMaxPages; page++)
        {
            var result = await GraphqlAsync(
                query,
                new Dictionary<string, object?>
                {
                    ["owner"] = owner,
                    ["name"] = name,
                    ["states"] = States(kind, state),
                    ["after"] = after,
                },
                ct);

            if (!result.Success)
            {
                return GitHubResult<GitHubWorkList>.Fail(result.Error!, result.Failure);
            }

            GitHubWorkList? parsed;
            try
            {
                parsed = ParseWorkList(result.Value!, field, kind);
            }
            catch (JsonException)
            {
                return GitHubResult<GitHubWorkList>.Fail("GitHub sent a reply GitApp could not read.");
            }

            if (parsed is null)
            {
                return GitHubResult<GitHubWorkList>.Fail(
                    "GitHub could not find that. It may be private, renamed, or deleted.",
                    GitHubFailure.NotFound);
            }

            total = parsed.TotalCount;
            items.AddRange(parsed.Items);

            if (!parsed.Truncated)
            {
                truncated = false;
                break;
            }

            after = NextCursor(result.Value!, field);
            if (string.IsNullOrEmpty(after))
            {
                truncated = true;
                break;
            }

            truncated = true;
        }

        return GitHubResult<GitHubWorkList>.Ok(new GitHubWorkList(items, total, truncated && items.Count < total));
    }

    internal static GitHubWorkList? ParseWorkList(string json, string field, GitHubWorkKind kind)
    {
        using var document = JsonDocument.Parse(json);
        if (!TryRepo(document.RootElement, out var repo))
        {
            return null;
        }

        if (!repo.TryGetProperty(field, out var list) || list.ValueKind != JsonValueKind.Object)
        {
            return new GitHubWorkList(Array.Empty<GitHubWorkItem>(), 0, false);
        }

        var total = Int(list, "totalCount");
        var items = new List<GitHubWorkItem>();

        if (list.TryGetProperty("nodes", out var nodes) && nodes.ValueKind == JsonValueKind.Array)
        {
            foreach (var node in nodes.EnumerateArray())
            {
                var item = ReadWorkItem(node, kind);
                if (item is not null)
                {
                    items.Add(item);
                }
            }
        }

        var truncated = false;
        if (list.TryGetProperty("pageInfo", out var page) && Bool(page, "hasNextPage"))
        {
            truncated = true;
        }

        return new GitHubWorkList(items, total, truncated);
    }

    internal static GitHubWorkDetail? ParseWorkDetail(string json, string field, GitHubWorkKind kind)
    {
        using var document = JsonDocument.Parse(json);
        if (!TryRepo(document.RootElement, out var repo))
        {
            return null;
        }

        if (!repo.TryGetProperty(field, out var node) || node.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var item = ReadWorkItem(node, kind);
        if (item is null)
        {
            return null;
        }

        var comments = new List<GitHubComment>();
        var commentTotal = 0;
        if (node.TryGetProperty("comments", out var commentWrap) && commentWrap.ValueKind == JsonValueKind.Object)
        {
            commentTotal = Int(commentWrap, "totalCount");
            if (commentWrap.TryGetProperty("nodes", out var commentNodes)
                && commentNodes.ValueKind == JsonValueKind.Array)
            {
                foreach (var comment in commentNodes.EnumerateArray())
                {
                    comments.Add(new GitHubComment(
                        ActorLogin(comment),
                        Date(comment, "createdAt") ?? DateTimeOffset.UtcNow,
                        String(comment, "body") ?? string.Empty));
                }
            }
        }

        var assignees = Names(node, "assignees", "login");
        string? milestone = null;
        if (node.TryGetProperty("milestone", out var mile) && mile.ValueKind == JsonValueKind.Object)
        {
            milestone = String(mile, "title");
        }

        var commits = 0;
        if (node.TryGetProperty("commits", out var commitsEl))
        {
            commits = Int(commitsEl, "totalCount");
        }

        return new GitHubWorkDetail(
            Item: item,
            BodyMarkdown: String(node, "body"),
            CreatedAt: Date(node, "createdAt") ?? item.UpdatedAt,
            Assignees: assignees,
            Milestone: milestone,
            Comments: comments,
            CommentTotal: commentTotal,
            CommitCount: commits,
            ChangedFiles: Int(node, "changedFiles"),
            Additions: Int(node, "additions"),
            Deletions: Int(node, "deletions"),
            Mergeable: String(node, "mergeable"));
    }

    internal static GitHubWorkItem? ReadWorkItem(JsonElement node, GitHubWorkKind kind)
    {
        var number = Int(node, "number");
        var title = String(node, "title");
        if (number <= 0 || string.IsNullOrEmpty(title))
        {
            return null;
        }

        var labels = Names(node, "labels", "name");
        var comments = 0;
        if (node.TryGetProperty("comments", out var commentWrap))
        {
            comments = Int(commentWrap, "totalCount");
        }

        return new GitHubWorkItem(
            Number: number,
            Title: title,
            Kind: kind,
            State: ReadState(node),
            IsDraft: Bool(node, "isDraft"),
            Author: ActorLogin(node),
            UpdatedAt: Date(node, "updatedAt") ?? DateTimeOffset.UtcNow,
            CommentCount: comments,
            Labels: labels,
            HtmlUrl: String(node, "url") ?? string.Empty,
            HeadRef: String(node, "headRefName"),
            BaseRef: String(node, "baseRefName"));
    }

    internal static GitHubItemState ReadState(JsonElement node)
    {
        if (Bool(node, "merged"))
        {
            return GitHubItemState.Merged;
        }

        return String(node, "state") switch
        {
            "MERGED" => GitHubItemState.Merged,
            "CLOSED" => GitHubItemState.Closed,
            _ => GitHubItemState.Open,
        };
    }

    internal static string? ActorLogin(JsonElement parent)
    {
        if (!parent.TryGetProperty("author", out var actor) || actor.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return String(actor, "login");
    }

    internal static IReadOnlyList<string> Names(JsonElement parent, string collection, string field)
    {
        var names = new List<string>();
        if (!parent.TryGetProperty(collection, out var wrap)
            || !wrap.TryGetProperty("nodes", out var nodes)
            || nodes.ValueKind != JsonValueKind.Array)
        {
            return names;
        }

        foreach (var node in nodes.EnumerateArray())
        {
            if (String(node, field) is { Length: > 0 } name)
            {
                names.Add(name);
            }
        }

        return names;
    }

    private static string[]? States(GitHubWorkKind kind, GitHubItemState? state)
    {
        if (state is null)
        {
            return null;
        }

        if (kind == GitHubWorkKind.PullRequest)
        {
            return state switch
            {
                GitHubItemState.Merged => new[] { "MERGED" },
                GitHubItemState.Closed => new[] { "CLOSED" },
                _ => new[] { "OPEN" },
            };
        }

        return state == GitHubItemState.Closed
            ? new[] { "CLOSED" }
            : new[] { "OPEN" };
    }

    private static string? NextCursor(string json, string field)
    {
        using var document = JsonDocument.Parse(json);
        if (!TryRepo(document.RootElement, out var repo)
            || !repo.TryGetProperty(field, out var list)
            || !list.TryGetProperty("pageInfo", out var page))
        {
            return null;
        }

        return String(page, "endCursor");
    }
}
