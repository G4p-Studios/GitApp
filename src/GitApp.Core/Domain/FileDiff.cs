namespace GitApp.Domain;

public enum DiffLineKind
{
    /// <summary>Present in both sides. Context.</summary>
    Unchanged,
    Added,
    Removed,
}

/// <summary>
/// One line of a diff, already carrying everything needed to announce it.
///
/// The announcement logic lives here rather than in the view because it is
/// the part that has to be right, and here it can be tested. See
/// docs/DIFF-VIEWER.md.
/// </summary>
public sealed record DiffLine(
    DiffLineKind Kind,
    string Content,
    int? OriginalLineNumber,
    int? ModifiedLineNumber)
{
    /// <summary>
    /// An empty line has to say something. Silence is indistinguishable from
    /// a row that failed to read.
    /// </summary>
    private string SpokenContent =>
        Content.Trim().Length == 0 ? "blank" : Content;

    /// <summary>
    /// Content first, classification after.
    ///
    /// Leading with "added" would open twenty consecutive added lines with
    /// the same word, making the listener wait through it each time to reach
    /// the part that differs. Content is what distinguishes one row from the
    /// next. See docs/DIFF-VIEWER.md.
    ///
    /// The words "added" and "removed" rather than the symbols "+" and "-",
    /// because most screen readers do not speak those at default punctuation
    /// levels, which would leave the most important fact about the row
    /// silent.
    /// </summary>
    public string AccessibleName => Kind switch
    {
        DiffLineKind.Added =>
            $"{SpokenContent}, added, modified line {ModifiedLineNumber}",

        DiffLineKind.Removed =>
            $"{SpokenContent}, removed, original line {OriginalLineNumber}",

        // When a line has moved, both numbers matter: that pair is what lets
        // a listener map what they hear back onto the two files.
        _ when OriginalLineNumber != ModifiedLineNumber =>
            $"{SpokenContent}, original line {OriginalLineNumber}, modified line {ModifiedLineNumber}",

        _ => $"{SpokenContent}, unchanged line {OriginalLineNumber}",
    };

    /// <summary>The gutter text shown beside the line.</summary>
    public string Marker => Kind switch
    {
        DiffLineKind.Added => "+",
        DiffLineKind.Removed => "-",
        _ => " ",
    };

    public string LineNumberDisplay => Kind switch
    {
        DiffLineKind.Added => ModifiedLineNumber?.ToString() ?? string.Empty,
        DiffLineKind.Removed => OriginalLineNumber?.ToString() ?? string.Empty,
        _ => ModifiedLineNumber?.ToString() ?? string.Empty,
    };
}

/// <summary>
/// A block of related changes, with its surrounding context.
/// </summary>
public sealed record DiffHunk(
    int OriginalStart,
    int OriginalCount,
    int ModifiedStart,
    int ModifiedCount,
    IReadOnlyList<DiffLine> Lines)
{
    public int AddedCount => Lines.Count(l => l.Kind == DiffLineKind.Added);

    public int RemovedCount => Lines.Count(l => l.Kind == DiffLineKind.Removed);

    /// <summary>
    /// The header announcement, following VS Code's wording because it is
    /// well judged: position in the whole first, since that is what the
    /// scrollbar was telling a sighted reader.
    ///
    /// <paramref name="state"/> is "collapsed" or "expanded" for a hunk big
    /// enough to be foldable, and null for one that is always shown. It sits
    /// straight after the position because it is the actionable fact, and
    /// because that is where a tree view reports the same thing.
    /// </summary>
    public string AccessibleName(int index, int total, string? state = null) =>
        $"Difference {index} of {total}, " +
        (state is null ? string.Empty : $"{state}, ") +
        $"{Side(OriginalStart, OriginalCount, "original", "nothing in the original")}, " +
        $"{Side(ModifiedStart, ModifiedCount, "modified", "nothing in the modified file")}";

    /// <summary>
    /// One half of the header.
    ///
    /// A side with no lines is a new or deleted file, and "original line 0,
    /// no lines changed" is both untrue and confusing. Saying what it is
    /// beats leaving the listener to infer it from an absence, which is the
    /// one thing speech cannot convey.
    /// </summary>
    private static string Side(int start, int count, string which, string empty) =>
        count == 0 ? empty : $"{which} line {start}, {Pluralise(count)}";

    public string Display => $"@@ -{OriginalStart},{OriginalCount} +{ModifiedStart},{ModifiedCount} @@";

    private static string Pluralise(int lines) => lines switch
    {
        0 => "no lines changed",
        1 => "1 line changed",
        _ => $"{lines} lines changed",
    };
}

/// <summary>
/// The diff for one file.
/// </summary>
public sealed record FileDiff(
    string Path,
    IReadOnlyList<DiffHunk> Hunks,
    bool IsBinary = false,
    string? OldPath = null)
{
    public static FileDiff Empty { get; } = new(string.Empty, Array.Empty<DiffHunk>());

    public bool HasChanges => Hunks.Count > 0 || IsBinary;

    public int AddedCount => Hunks.Sum(h => h.AddedCount);

    public int RemovedCount => Hunks.Sum(h => h.RemovedCount);

    /// <summary>
    /// Spoken when the diff is opened, so the listener knows the size of
    /// what they are about to move through before they start.
    /// </summary>
    public string Summary
    {
        get
        {
            if (IsBinary)
            {
                return $"{System.IO.Path.GetFileName(Path)}, binary file, no text diff available";
            }

            if (Hunks.Count == 0)
            {
                return $"{System.IO.Path.GetFileName(Path)}, no changes";
            }

            var differences = Hunks.Count == 1 ? "1 difference" : $"{Hunks.Count} differences";
            var added = AddedCount == 1 ? "1 line added" : $"{AddedCount} lines added";
            var removed = RemovedCount == 1 ? "1 line removed" : $"{RemovedCount} lines removed";

            return $"{System.IO.Path.GetFileName(Path)}, {differences}, {added}, {removed}";
        }
    }
}
