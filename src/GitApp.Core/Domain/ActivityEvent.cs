namespace GitApp.Domain;

/// <summary>
/// One row in the Home feed: something a person you follow, or a repository
/// you watch, just did.
///
/// The name leads with the actor, matching github.com's dashboard, because
/// that is what distinguishes one row from the next in a mixed feed. A
/// run of stars that all opened with "starred" would make the listener wait
/// through the same word every time (ARCHITECTURE 3.3).
/// </summary>
public sealed record ActivityEvent(
    string Id,
    string Type,
    string Actor,
    string? Repository,
    string Summary,
    DateTimeOffset CreatedAt,
    string? HtmlUrl)
{
    public string AccessibleName =>
        $"{Summary}, {RelativeTime.From(CreatedAt)}";

    /// <summary>The second line on screen, not spoken separately.</summary>
    public string Detail => RelativeTime.From(CreatedAt);

    public string? Owner => SplitRepository(Repository).Owner;

    public string? RepoName => SplitRepository(Repository).Name;

    /// <summary>
    /// Turn a GitHub event payload into a sentence. Unknown types fall back
    /// to the type name with the word "Event" dropped and underscores spoken
    /// as spaces, so a new event still reads rather than being skipped.
    /// </summary>
    public static string Sentence(
        string type,
        string actor,
        string? repository,
        string? action,
        string? refType,
        string? gitRef,
        string? title,
        int? number)
    {
        var repo = string.IsNullOrEmpty(repository) ? null : repository;
        var branch = BranchName(gitRef);

        return type switch
        {
            "WatchEvent" => repo is null
                ? $"{actor} starred a repository"
                : $"{actor} starred {repo}",
            "ForkEvent" => repo is null
                ? $"{actor} forked a repository"
                : $"{actor} forked {repo}",
            "FollowEvent" => repo is null
                ? $"{actor} followed someone"
                : $"{actor} followed {repo.Split('/')[0]}",
            "PublicEvent" => repo is null
                ? $"{actor} made a repository public"
                : $"{actor} made {repo} public",
            "CreateEvent" when refType == "repository" => repo is null
                ? $"{actor} created a repository"
                : $"{actor} created the repository {repo}",
            "CreateEvent" when refType == "branch" && branch is not null && repo is not null =>
                $"{actor} created the branch {branch} in {repo}",
            "CreateEvent" when refType == "tag" && gitRef is not null && repo is not null =>
                $"{actor} created the tag {gitRef} in {repo}",
            "CreateEvent" => repo is null
                ? $"{actor} created something"
                : $"{actor} created something in {repo}",
            "PushEvent" when branch is not null && repo is not null =>
                $"{actor} pushed to {branch} in {repo}",
            "PushEvent" => repo is null
                ? $"{actor} pushed"
                : $"{actor} pushed to {repo}",
            "ReleaseEvent" => TitleClause(actor, "released", title, repo, number),
            "IssuesEvent" => TitleClause(actor, IssueVerb(action), title, repo, number),
            "IssueCommentEvent" => TitleClause(actor, "commented on", title, repo, number),
            "PullRequestEvent" => TitleClause(actor, PullVerb(action), title, repo, number),
            "PullRequestReviewEvent" => TitleClause(actor, "reviewed", title, repo, number),
            "PullRequestReviewCommentEvent" => TitleClause(actor, "commented on", title, repo, number),
            "MemberEvent" => repo is null
                ? $"{actor} changed repository access"
                : $"{actor} changed access on {repo}",
            "GollumEvent" => repo is null
                ? $"{actor} edited a wiki"
                : $"{actor} edited the wiki in {repo}",
            "CommitCommentEvent" => repo is null
                ? $"{actor} commented on a commit"
                : $"{actor} commented on a commit in {repo}",
            _ => Fallback(type, actor, repo),
        };
    }

    private static string TitleClause(
        string actor, string verb, string? title, string? repo, int? number)
    {
        var subject = title?.Trim();
        if (string.IsNullOrEmpty(subject))
        {
            subject = number is > 0 ? $"number {number}" : "something";
        }
        else if (number is > 0)
        {
            subject = $"{subject}, number {number}";
        }

        return repo is null
            ? $"{actor} {verb} {subject}"
            : $"{actor} {verb} {subject}, in {repo}";
    }

    private static string IssueVerb(string? action) => action switch
    {
        "closed" => "closed issue",
        "reopened" => "reopened issue",
        "assigned" => "was assigned issue",
        "unassigned" => "was unassigned issue",
        "labeled" or "unlabeled" => "updated issue",
        _ => "opened issue",
    };

    private static string PullVerb(string? action) => action switch
    {
        "closed" => "closed pull request",
        "reopened" => "reopened pull request",
        "review_requested" => "requested a review on",
        "ready_for_review" => "marked ready for review",
        _ => "opened pull request",
    };

    private static string Fallback(string type, string actor, string? repo)
    {
        var body = type.EndsWith("Event", StringComparison.Ordinal)
            ? type[..^5]
            : type;
        var words = new System.Text.StringBuilder();
        foreach (var ch in body)
        {
            if (char.IsUpper(ch) && words.Length > 0)
            {
                words.Append(' ');
            }

            words.Append(char.ToLowerInvariant(ch));
        }

        return repo is null
            ? $"{actor} {words}"
            : $"{actor} {words} in {repo}";
    }

    private static string? BranchName(string? gitRef)
    {
        if (string.IsNullOrEmpty(gitRef))
        {
            return null;
        }

        const string heads = "refs/heads/";
        return gitRef.StartsWith(heads, StringComparison.Ordinal)
            ? gitRef[heads.Length..]
            : gitRef;
    }

    private static (string? Owner, string? Name) SplitRepository(string? full)
    {
        if (string.IsNullOrEmpty(full))
        {
            return (null, null);
        }

        var slash = full.IndexOf('/');
        if (slash <= 0 || slash == full.Length - 1)
        {
            return (null, full);
        }

        return (full[..slash], full[(slash + 1)..]);
    }
}
