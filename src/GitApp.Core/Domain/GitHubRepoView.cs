namespace GitApp.Domain;

/// <summary>
/// What kind of thing a row in the remote file table is.
///
/// Spoken as a word after the name, never as an icon or a trailing slash:
/// "docs, folder" not "docs/" and not a silent glyph.
/// </summary>
public enum GitHubEntryKind
{
    File,
    Folder,
    Submodule,
    Parent,
}

/// <summary>
/// One row in the repository file table.
/// </summary>
public sealed record GitHubTreeEntry(
    string Name,
    GitHubEntryKind Kind,
    string Path,
    string? LastSubject = null,
    DateTimeOffset? LastTouched = null)
{
    /// <summary>
    /// The row you go to in order to leave a directory. First in the list
    /// whenever the path is not the repository root, matching Explorer and
    /// github.com's <c>..</c>.
    /// </summary>
    public static GitHubTreeEntry Parent(string parentPath) =>
        new("..", GitHubEntryKind.Parent, parentPath);

    public bool IsFolder => Kind is GitHubEntryKind.Folder or GitHubEntryKind.Parent;

    /// <summary>
    /// Name first, then the kind, then the last-touch commit if we have it.
    ///
    /// The last-touch subject is the most distinctive thing about a file
    /// after its name, so it stays in the spoken row rather than behind a
    /// key. It is also the longest part; a listen-through may yet move it,
    /// but omitting it before that is guessing. See docs/REPOSITORY-VIEW.md.
    /// </summary>
    public string AccessibleName
    {
        get
        {
            if (Kind == GitHubEntryKind.Parent)
            {
                return "Parent folder";
            }

            var parts = new List<string> { Name, KindWord };

            if (!string.IsNullOrWhiteSpace(LastSubject))
            {
                parts.Add(LastSubject.Trim());
            }

            if (LastTouched is { } when)
            {
                parts.Add(RelativeTime.From(when));
            }

            return string.Join(", ", parts);
        }
    }

    public string KindWord => Kind switch
    {
        GitHubEntryKind.Folder => "folder",
        GitHubEntryKind.Submodule => "submodule",
        GitHubEntryKind.Parent => "parent folder",
        _ => "file",
    };

    /// <summary>The second line shown on screen, not spoken separately.</summary>
    public string Detail
    {
        get
        {
            if (Kind == GitHubEntryKind.Parent)
            {
                return "Go up one folder";
            }

            var parts = new List<string>();

            if (!string.IsNullOrWhiteSpace(LastSubject))
            {
                parts.Add(LastSubject.Trim());
            }

            if (LastTouched is { } when)
            {
                parts.Add(RelativeTime.From(when));
            }

            return parts.Count == 0 ? KindWord : string.Join(" · ", parts);
        }
    }
}

/// <summary>
/// The commit shown above the file table: the tip of the current branch.
/// </summary>
public sealed record GitHubLatestCommit(
    string ShortHash,
    string Subject,
    IReadOnlyList<string> Authors,
    DateTimeOffset CommittedAt,
    int CommitCount,
    string Branch)
{
    public string Byline => Authors.Count switch
    {
        0 => string.Empty,
        1 => $"by {Authors[0]}",
        2 => $"by {Authors[0]} and {Authors[1]}",
        _ => $"by {string.Join(", ", Authors.Take(Authors.Count - 1))} and {Authors[^1]}",
    };

    /// <summary>
    /// Subject first: it is what distinguishes this commit, and leading
    /// with the hash would make the line open with characters read one
    /// by one. See ARCHITECTURE 3.3.
    /// </summary>
    public string AccessibleName
    {
        get
        {
            var parts = new List<string> { Subject };

            if (Byline.Length > 0)
            {
                parts.Add(Byline);
            }

            parts.Add(RelativeTime.From(CommittedAt));
            parts.Add(ShortHash);

            var commits = CommitCount == 1
                ? $"1 commit on {Branch}"
                : $"{CommitCount} commits on {Branch}";
            parts.Add(commits);

            return string.Join(", ", parts);
        }
    }

    public string Detail =>
        string.Join(" · ", new[] { Byline, RelativeTime.From(CommittedAt), ShortHash }
            .Where(s => s.Length > 0));
}

/// <summary>
/// A branch you can switch the file table to.
/// </summary>
public sealed record GitHubBranch(string Name, bool IsDefault)
{
    public string AccessibleName => IsDefault ? $"{Name}, default" : Name;
}

/// <summary>
/// Everything the repository screen shows, as one snapshot.
///
/// Loaded as a whole before the lists are filled, because partial data
/// arriving in waves re-renders the file table and moves focus. See
/// ARCHITECTURE 4.3.
/// </summary>
public sealed record GitHubRepoView(
    string Name,
    string Owner,
    string? Description,
    bool IsPrivate,
    IReadOnlyList<string> Topics,
    string? Language,
    int Stars,
    int Watchers,
    int Forks,
    int ReleaseCount,
    int BranchCount,
    int TagCount,
    int OpenIssueCount,
    int OpenPullRequestCount,
    string? HomepageUrl,
    string HtmlUrl,
    string CurrentBranch,
    string? DefaultBranch,
    string Path,
    string? HeadOid,
    GitHubLatestCommit? Latest,
    IReadOnlyList<GitHubBranch> Branches,
    IReadOnlyList<GitHubTreeEntry> Entries,
    string? ReadmeName,
    string? ReadmeMarkdown)
{
    public string FullName => $"{Owner}/{Name}";

    public string Title => $"{FullName}, {(IsPrivate ? "private" : "public")}";

    public string PathDescription =>
        string.IsNullOrEmpty(Path) ? FullName : $"{FullName}/{Path}";

    public string EntrySummary
    {
        get
        {
            var real = Entries.Count(e => e.Kind != GitHubEntryKind.Parent);
            var where = string.IsNullOrEmpty(Path) ? FullName : Path;

            return real == 1
                ? $"1 item in {where}"
                : $"{real} items in {where}";
        }
    }

    /// <summary>
    /// The releases fact, named on its own because it is the one About
    /// fact that is also a door: on github.com the sidebar's Releases
    /// section opens the releases list, and so does this one. The page
    /// matches on this string rather than searching for "release".
    /// </summary>
    public string ReleasesFact => ReleaseCount == 0
        ? "No releases"
        : ReleaseCount == 1 ? "1 release" : $"{ReleaseCount} releases";

    /// <summary>
    /// The About pane, as one string per fact so each can be a control
    /// of its own. Empty facts are omitted rather than spoken as zero,
    /// except stars/watchers/forks/releases, where zero is information.
    /// </summary>
    public IReadOnlyList<string> AboutFacts
    {
        get
        {
            var facts = new List<string>
            {
                Title,
            };

            if (!string.IsNullOrWhiteSpace(Description))
            {
                facts.Add(Description.Trim());
            }

            if (Topics.Count > 0)
            {
                facts.Add("Topics, " + string.Join(", ", Topics));
            }

            if (!string.IsNullOrWhiteSpace(Language))
            {
                facts.Add(Language!);
            }

            facts.Add(Stars == 1 ? "1 star" : $"{Stars} stars");
            facts.Add(Watchers == 1 ? "1 watcher" : $"{Watchers} watchers");
            facts.Add(Forks == 1 ? "1 fork" : $"{Forks} forks");
            facts.Add(ReleasesFact);

            facts.Add(OpenIssueCount == 1 ? "1 open issue" : $"{OpenIssueCount} open issues");
            facts.Add(OpenPullRequestCount == 1
                ? "1 open pull request"
                : $"{OpenPullRequestCount} open pull requests");

            var branches = BranchCount == 1 ? "1 branch" : $"{BranchCount} branches";
            var tags = TagCount == 1 ? "1 tag" : $"{TagCount} tags";
            facts.Add($"{branches}, {tags}");

            if (!string.IsNullOrWhiteSpace(HomepageUrl))
            {
                facts.Add($"Website, {HomepageUrl}");
            }

            return facts;
        }
    }
}
