namespace GitApp.Domain;

/// <summary>
/// What happened to one file.
///
/// Git tracks two states per path: what is staged (the index) and what is not
/// (the working tree). A file can be both, for example partially staged, and
/// the UI has to be able to say so.
/// </summary>
public enum ChangeKind
{
    Unmodified,
    Added,
    Modified,
    Deleted,
    Renamed,
    Copied,
    Untracked,
    Ignored,
    Conflicted,
}

/// <summary>One changed path in the working tree.</summary>
public sealed record FileChange(
    string Path,
    ChangeKind Staged,
    ChangeKind Unstaged,
    string? OriginalPath = null)
{
    public bool IsStaged => Staged is not (ChangeKind.Unmodified or ChangeKind.Untracked);

    public bool IsConflicted => Staged == ChangeKind.Conflicted || Unstaged == ChangeKind.Conflicted;

    /// <summary>Just the file name, for the primary line in a row.</summary>
    public string FileName => System.IO.Path.GetFileName(Path);

    /// <summary>The containing directory, or empty at the repository root.</summary>
    public string Directory
    {
        get
        {
            var dir = System.IO.Path.GetDirectoryName(Path);
            return string.IsNullOrEmpty(dir) ? string.Empty : dir.Replace('\\', '/');
        }
    }

    /// <summary>
    /// A short human phrase for the state: "modified", "staged, modified".
    /// </summary>
    public string StateDescription
    {
        get
        {
            if (IsConflicted)
            {
                return "conflicted";
            }

            if (Unstaged == ChangeKind.Untracked)
            {
                return "untracked";
            }

            var parts = new List<string>(2);

            if (IsStaged)
            {
                parts.Add($"staged {Describe(Staged)}");
            }

            if (Unstaged is not ChangeKind.Unmodified and not ChangeKind.Untracked)
            {
                parts.Add(Describe(Unstaged));
            }

            return parts.Count == 0 ? "unchanged" : string.Join(", ", parts);
        }
    }

    /// <summary>
    /// The whole row as one string, in reading order.
    ///
    /// A row is announced as a single label, so the columns are flattened
    /// here. Path first because that is what distinguishes one row from the
    /// next; state after, because it is the same handful of words repeated
    /// down the list and leading with it would make every row sound alike.
    /// See docs/ARCHITECTURE.md 3.3.
    /// </summary>
    public string AccessibleName =>
        string.IsNullOrEmpty(Directory)
            ? $"{FileName}, {StateDescription}"
            : $"{FileName}, in {Directory}, {StateDescription}";

    private static string Describe(ChangeKind kind) => kind switch
    {
        ChangeKind.Added => "added",
        ChangeKind.Modified => "modified",
        ChangeKind.Deleted => "deleted",
        ChangeKind.Renamed => "renamed",
        ChangeKind.Copied => "copied",
        ChangeKind.Untracked => "untracked",
        ChangeKind.Ignored => "ignored",
        ChangeKind.Conflicted => "conflicted",
        _ => "unchanged",
    };
}

/// <summary>
/// The state of a working tree at a point in time.
/// </summary>
public sealed record RepoStatus(
    string Branch,
    string? Upstream,
    int Ahead,
    int Behind,
    IReadOnlyList<FileChange> Changes,
    bool IsDetachedHead = false)
{
    public static RepoStatus Empty { get; } =
        new("unknown", null, 0, 0, Array.Empty<FileChange>());

    public IReadOnlyList<FileChange> Staged =>
        Changes.Where(c => c.IsStaged && !c.IsConflicted).ToList();

    public IReadOnlyList<FileChange> Unstaged =>
        Changes.Where(c => !c.IsStaged && !c.IsConflicted).ToList();

    public IReadOnlyList<FileChange> Conflicted =>
        Changes.Where(c => c.IsConflicted).ToList();

    public bool HasChanges => Changes.Count > 0;

    public bool CanCommit => Staged.Count > 0 && Conflicted.Count == 0;

    /// <summary>
    /// Sync state in words: "up to date", "2 ahead", "1 ahead, 3 behind".
    ///
    /// Deliberately words rather than symbols. Arrow glyphs and counts read
    /// badly, inconsistently, or not at all depending on the screen reader
    /// and its punctuation level.
    /// </summary>
    public string SyncDescription
    {
        get
        {
            if (IsDetachedHead)
            {
                return "detached head";
            }

            if (Upstream is null)
            {
                return "no upstream";
            }

            if (Ahead == 0 && Behind == 0)
            {
                return "up to date";
            }

            var parts = new List<string>(2);
            if (Ahead > 0)
            {
                parts.Add($"{Ahead} ahead");
            }

            if (Behind > 0)
            {
                parts.Add($"{Behind} behind");
            }

            return string.Join(", ", parts);
        }
    }

    /// <summary>A summary of the working tree, for the status line.</summary>
    public string ChangeSummary => Changes.Count switch
    {
        0 => "no changes",
        1 => "1 change",
        _ => $"{Changes.Count} changes",
    };
}
