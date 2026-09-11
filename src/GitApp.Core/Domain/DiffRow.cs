namespace GitApp.Domain;

/// <summary>
/// One row of the diff viewer: either a hunk header or a line.
///
/// Headers and lines share a list rather than sitting in separate controls,
/// so the whole diff can be read straight through with Down, and so the set
/// positions a screen reader reports cover the entire diff rather than
/// restarting at each hunk. See docs/DIFF-VIEWER.md.
///
/// This lives in the core library, not the view, for the same reason the
/// announcement strings do: it is the part that has to be right, and here it
/// can be tested without starting a window.
/// </summary>
public sealed record DiffRow(
    string AccessibleName,
    string Display,
    string Marker,
    string LineNumber,
    bool IsHeader,
    int HunkIndex = -1,
    bool IsExpandable = false,
    bool IsExpanded = true)
{
    /// <summary>
    /// Above this many lines a hunk starts folded.
    ///
    /// A 400-line hunk is technically navigable and practically not: it is
    /// four hundred arrow presses to find out whether the next difference is
    /// worth reading. Below the threshold folding is pure friction, since
    /// arrowing through twenty lines is faster than deciding to.
    /// </summary>
    public const int DefaultLargeHunkLines = 40;

    public static DiffRow FromLine(DiffLine line) =>
        new(
            AccessibleName: line.AccessibleName,
            // The raw content, not the spoken form: a sighted reader wants
            // the actual line, including when it is empty.
            Display: line.Content,
            Marker: line.Marker,
            LineNumber: line.LineNumberDisplay,
            IsHeader: false);

    public static DiffRow FromHeader(
        DiffHunk hunk,
        int index,
        int total,
        bool expandable = false,
        bool expanded = true) =>
        new(
            AccessibleName: hunk.AccessibleName(
                index, total, expandable ? (expanded ? "expanded" : "collapsed") : null),
            Display: expandable
                ? $"{hunk.Display}  {hunk.Lines.Count} lines"
                : hunk.Display,
            // Visual only. The state is in the name as a word, because a
            // glyph is silent at default punctuation levels.
            Marker: expandable ? (expanded ? "▾" : "▸") : string.Empty,
            LineNumber: string.Empty,
            IsHeader: true,
            HunkIndex: index - 1,
            IsExpandable: expandable,
            IsExpanded: expanded);

    /// <summary>
    /// Flatten a file diff into the rows that are currently visible.
    ///
    /// Hunks longer than <paramref name="largeHunkLines"/> are folded unless
    /// their index appears in <paramref name="expanded"/>. A folded hunk
    /// still contributes its header, so the shape of the whole file can be
    /// read in a few keystrokes before committing to any one part of it.
    /// </summary>
    public static IReadOnlyList<DiffRow> Build(
        FileDiff diff,
        IReadOnlySet<int>? expanded = null,
        int largeHunkLines = DefaultLargeHunkLines)
    {
        var rows = new List<DiffRow>();

        for (var i = 0; i < diff.Hunks.Count; i++)
        {
            var hunk = diff.Hunks[i];
            var expandable = hunk.Lines.Count > largeHunkLines;
            var open = !expandable || (expanded?.Contains(i) ?? false);

            rows.Add(FromHeader(hunk, i + 1, diff.Hunks.Count, expandable, open));

            if (open)
            {
                rows.AddRange(hunk.Lines.Select(FromLine));
            }
        }

        return rows;
    }

    /// <summary>The hunks that would start folded, for the opening summary.</summary>
    public static int CountFolded(
        FileDiff diff,
        IReadOnlySet<int>? expanded = null,
        int largeHunkLines = DefaultLargeHunkLines) =>
        diff.Hunks.Where((h, i) =>
            h.Lines.Count > largeHunkLines && !(expanded?.Contains(i) ?? false)).Count();
}
