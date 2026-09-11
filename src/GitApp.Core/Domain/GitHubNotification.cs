namespace GitApp.Domain;

/// <summary>
/// What a notification is about. GitHub's <c>subject.type</c> is a handful of
/// strings; these are the ones the inbox knows how to open, plus a catch-all
/// so an unfamiliar one still reads as words rather than disappearing.
/// </summary>
public enum GitHubSubjectKind
{
    Issue,
    PullRequest,
    Release,
    Commit,
    Discussion,
    Other,
}

/// <summary>
/// Turning GitHub's <c>reason</c> tokens into words.
///
/// The API sends <c>review_requested</c>, <c>state_change</c>,
/// <c>team_mention</c>. A screen reader reads <c>review_requested</c> as
/// "review underscore requested", and the underscore is not something the
/// listener can act on. Every reason the API documents has a phrase here, and
/// an unknown one falls back to its own words with the underscores spoken as
/// spaces rather than as the word "underscore".
/// </summary>
public static class NotificationReason
{
    public static string Word(string? reason) => reason switch
    {
        "assign" => "assigned to you",
        "author" => "you opened this",
        "comment" => "new comment",
        "ci_activity" => "workflow run",
        "invitation" => "you were invited",
        "manual" => "you subscribed",
        "mention" => "you were mentioned",
        "push" => "new commits",
        "review_requested" => "review requested",
        "security_alert" => "security alert",
        "state_change" => "closed or reopened",
        "subscribed" => "you are watching this",
        "team_mention" => "your team was mentioned",
        "your_activity" => "your activity",
        null or "" => "notification",
        _ => reason.Replace('_', ' '),
    };
}

/// <summary>
/// One notification, as a row in the inbox.
///
/// Whether it is unread is deliberately <em>not</em> part of
/// <see cref="AccessibleName"/>. Unread flips to read the moment the user
/// opens the thread, and ARCHITECTURE 3.3 is explicit that state which ticks
/// underneath a row must not live in the row's own name, or the screen reader
/// re-reads the whole row every time it changes. It is exposed separately, as
/// <see cref="UnreadIndicator"/>, for a distinct named element in the row.
/// </summary>
public sealed record GitHubNotification(
    string Id,
    string RepositoryFullName,
    string Title,
    GitHubSubjectKind SubjectKind,
    string Reason,
    bool Unread,
    DateTimeOffset UpdatedAt,
    string? Url = null)
{
    /// <summary>The reason, in words. See <see cref="NotificationReason"/>.</summary>
    public string ReasonWord => NotificationReason.Word(Reason);

    /// <summary>The subject type, in words a listener hears cleanly.</summary>
    public string SubjectWord => SubjectKind switch
    {
        GitHubSubjectKind.PullRequest => "pull request",
        GitHubSubjectKind.Issue => "issue",
        GitHubSubjectKind.Release => "release",
        GitHubSubjectKind.Commit => "commit",
        GitHubSubjectKind.Discussion => "discussion",
        _ => "notification",
    };

    /// <summary>
    /// Title first, because that is what distinguishes one notification from
    /// the next; leading with the reason or the repository would open every
    /// row in the same repository with the same words. Then why it arrived,
    /// then what it is, then where, then when. See ARCHITECTURE 3.3.
    /// </summary>
    public string AccessibleName
    {
        get
        {
            var parts = new List<string> { Title, ReasonWord, SubjectWord };

            if (!string.IsNullOrWhiteSpace(RepositoryFullName))
            {
                parts.Add($"in {RepositoryFullName}");
            }

            parts.Add(RelativeTime.From(UpdatedAt));

            return string.Join(", ", parts);
        }
    }

    /// <summary>
    /// The persistent unread flag, as its own row element rather than folded
    /// into the name. Empty when read, so the element says nothing.
    /// </summary>
    public string UnreadIndicator => Unread ? "unread" : string.Empty;

    /// <summary>The second line shown on screen, not spoken separately.</summary>
    public string Detail
    {
        get
        {
            var parts = new List<string> { ReasonWord };

            if (!string.IsNullOrWhiteSpace(RepositoryFullName))
            {
                parts.Add(RepositoryFullName);
            }

            parts.Add(RelativeTime.From(UpdatedAt));

            return string.Join(" · ", parts);
        }
    }
}

/// <summary>
/// The result of one poll of the notifications endpoint.
///
/// <see cref="NotModified"/> is the common case and the whole point of the
/// conditional request: GitHub answered 304, nothing changed, and it did not
/// count against the rate limit. <see cref="PollToken"/> is the opaque
/// <c>Last-Modified</c> value to echo back as <c>If-Modified-Since</c> next
/// time, and <see cref="PollIntervalSeconds"/> is GitHub's own floor on how
/// often this app may ask again (ARCHITECTURE 4.4).
/// </summary>
public sealed record NotificationPage(
    IReadOnlyList<GitHubNotification> Items,
    string? PollToken,
    int PollIntervalSeconds,
    bool NotModified)
{
    public static NotificationPage Unchanged(string? token, int intervalSeconds) =>
        new(Array.Empty<GitHubNotification>(), token, intervalSeconds, NotModified: true);

    /// <summary>Unread items only, the inbox's real content.</summary>
    public IReadOnlyList<GitHubNotification> Unread =>
        Items.Where(n => n.Unread).ToList();

    /// <summary>The heading over the inbox list, in words with a count.</summary>
    public string InboxHeading
    {
        get
        {
            var unread = Unread.Count;
            return unread switch
            {
                0 => "No unread notifications",
                1 => "1 unread notification",
                _ => $"{unread} unread notifications",
            };
        }
    }
}
