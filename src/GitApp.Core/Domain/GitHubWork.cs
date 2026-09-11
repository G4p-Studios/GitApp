namespace GitApp.Domain;

public enum GitHubWorkKind
{
    Issue,
    PullRequest,
}

public enum GitHubItemState
{
    Open,
    Closed,
    Merged,
}

/// <summary>
/// One row on the issues or pull requests list.
/// </summary>
public sealed record GitHubWorkItem(
    int Number,
    string Title,
    GitHubWorkKind Kind,
    GitHubItemState State,
    bool IsDraft,
    string? Author,
    DateTimeOffset UpdatedAt,
    int CommentCount,
    IReadOnlyList<string> Labels,
    string HtmlUrl,
    string? HeadRef = null,
    string? BaseRef = null)
{
    /// <summary>
    /// Title first: it is what distinguishes one row from another, and
    /// leading with "issue" would make every row open with the same word.
    /// The number is last, the way a commit hash is last, because it is
    /// confirmation rather than the thing you are scanning for.
    /// See ARCHITECTURE 3.3 and docs/ISSUES.md.
    /// </summary>
    public string AccessibleName
    {
        get
        {
            var parts = new List<string> { Title, StateWord };

            if (IsDraft)
            {
                parts.Add("draft");
            }

            if (Kind == GitHubWorkKind.PullRequest
                && !string.IsNullOrEmpty(HeadRef)
                && !string.IsNullOrEmpty(BaseRef))
            {
                parts.Add($"{HeadRef} into {BaseRef}");
            }

            parts.AddRange(Labels);

            if (!string.IsNullOrWhiteSpace(Author))
            {
                parts.Add($"by {Author}");
            }

            if (CommentCount == 1)
            {
                parts.Add("1 comment");
            }
            else if (CommentCount > 1)
            {
                parts.Add($"{CommentCount} comments");
            }

            parts.Add(RelativeTime.From(UpdatedAt));
            parts.Add(Kind == GitHubWorkKind.PullRequest
                ? $"pull request {Number}"
                : $"issue {Number}");

            return string.Join(", ", parts);
        }
    }

    public string StateWord => State switch
    {
        GitHubItemState.Merged => "merged",
        GitHubItemState.Closed => "closed",
        _ => "open",
    };

    public string Detail
    {
        get
        {
            var parts = new List<string> { StateWord };

            if (IsDraft)
            {
                parts.Add("draft");
            }

            if (Kind == GitHubWorkKind.PullRequest
                && !string.IsNullOrEmpty(HeadRef)
                && !string.IsNullOrEmpty(BaseRef))
            {
                parts.Add($"{HeadRef} into {BaseRef}");
            }

            if (!string.IsNullOrWhiteSpace(Author))
            {
                parts.Add(Author!);
            }

            parts.Add(RelativeTime.From(UpdatedAt));
            parts.Add($"#{Number}");

            return string.Join(" · ", parts);
        }
    }

    public string KindWord => Kind == GitHubWorkKind.PullRequest
        ? "pull request"
        : "issue";

    public string KindWordPlural => Kind == GitHubWorkKind.PullRequest
        ? "pull requests"
        : "issues";
}

/// <summary>
/// One comment on an issue or pull request. The opening post is not a
/// comment; it lives on <see cref="GitHubWorkDetail"/>.
/// </summary>
public sealed record GitHubComment(
    string? Author,
    DateTimeOffset CreatedAt,
    string BodyMarkdown)
{
    public string AuthorWord => string.IsNullOrWhiteSpace(Author) ? "ghost" : Author!;

    /// <summary>
    /// Author and time as the heading of this comment, so a screen reader
    /// jumping by heading lands at the start of each one.
    /// </summary>
    public string Heading => $"{AuthorWord}, {RelativeTime.From(CreatedAt)}";
}

/// <summary>
/// The conversation screen: title, body, comments, and the facts that
/// github.com puts in the sidebar.
/// </summary>
public sealed record GitHubWorkDetail(
    GitHubWorkItem Item,
    string? BodyMarkdown,
    DateTimeOffset CreatedAt,
    IReadOnlyList<string> Assignees,
    string? Milestone,
    IReadOnlyList<GitHubComment> Comments,
    int CommentTotal,
    int CommitCount = 0,
    int ChangedFiles = 0,
    int Additions = 0,
    int Deletions = 0,
    string? Mergeable = null)
{
    public string Title => Item.Title;

    public string Metadata
    {
        get
        {
            var parts = new List<string> { Item.StateWord };

            if (Item.IsDraft)
            {
                parts.Add("draft");
            }

            if (Item.Kind == GitHubWorkKind.PullRequest
                && !string.IsNullOrEmpty(Item.HeadRef)
                && !string.IsNullOrEmpty(Item.BaseRef))
            {
                parts.Add($"{Item.HeadRef} into {Item.BaseRef}");
            }

            if (!string.IsNullOrWhiteSpace(Item.Author))
            {
                parts.Add($"by {Item.Author}");
            }

            parts.Add($"created {RelativeTime.From(CreatedAt)}");
            parts.Add($"updated {RelativeTime.From(Item.UpdatedAt)}");
            parts.Add(Item.Kind == GitHubWorkKind.PullRequest
                ? $"pull request {Item.Number}"
                : $"issue {Item.Number}");

            return string.Join(", ", parts);
        }
    }

    public IReadOnlyList<string> AboutFacts
    {
        get
        {
            var facts = new List<string> { Item.StateWord };

            if (Item.IsDraft)
            {
                facts.Add("draft");
            }

            if (Item.Labels.Count > 0)
            {
                facts.Add("Labels, " + string.Join(", ", Item.Labels));
            }

            if (Assignees.Count > 0)
            {
                facts.Add(Assignees.Count == 1
                    ? $"Assigned to {Assignees[0]}"
                    : "Assigned to " + string.Join(", ", Assignees));
            }
            else
            {
                facts.Add("No one assigned");
            }

            facts.Add(string.IsNullOrWhiteSpace(Milestone)
                ? "No milestone"
                : $"Milestone, {Milestone}");

            if (Item.Kind == GitHubWorkKind.PullRequest)
            {
                if (!string.IsNullOrEmpty(Item.HeadRef) && !string.IsNullOrEmpty(Item.BaseRef))
                {
                    facts.Add($"{Item.HeadRef} into {Item.BaseRef}");
                }

                facts.Add(CommitCount == 1 ? "1 commit" : $"{CommitCount} commits");
                facts.Add(ChangedFiles == 1
                    ? "1 file changed"
                    : $"{ChangedFiles} files changed");
                facts.Add($"{Additions} added, {Deletions} removed");

                if (Item.State == GitHubItemState.Merged)
                {
                    facts.Add("merged");
                }
                else if (string.Equals(Mergeable, "CONFLICTING", StringComparison.OrdinalIgnoreCase))
                {
                    facts.Add("conflicts with the base branch");
                }
                else if (string.Equals(Mergeable, "MERGEABLE", StringComparison.OrdinalIgnoreCase))
                {
                    facts.Add("can be merged");
                }
            }

            if (CommentTotal > Comments.Count)
            {
                facts.Add($"Showing {Comments.Count} of {CommentTotal} comments");
            }

            return facts;
        }
    }

    public string CommentsHeading => Comments.Count == 0
        ? "No comments"
        : CommentTotal > Comments.Count
            ? $"Comments, showing {Comments.Count} of {CommentTotal}"
            : Comments.Count == 1 ? "1 comment" : $"{Comments.Count} comments";
}

/// <summary>
/// A page of issues or pull requests, with an honest count when GitHub
/// has more than we fetched.
/// </summary>
public sealed record GitHubWorkList(
    IReadOnlyList<GitHubWorkItem> Items,
    int TotalCount,
    bool Truncated);

/// <summary>
/// The state picker on the issues and pull requests screens.
/// </summary>
public sealed record WorkStateFilter(string Name, GitHubItemState? State)
{
    public string AccessibleName => Name;
}
