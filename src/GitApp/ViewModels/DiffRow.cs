using GitApp.Domain;

namespace GitApp.ViewModels;

/// <summary>
/// One row of the diff viewer: either a hunk header or a line.
///
/// Headers and lines share a list rather than sitting in separate controls,
/// so the whole diff can be read straight through with Down, and so the set
/// positions a screen reader reports cover the entire diff rather than
/// restarting at each hunk. See docs/DIFF-VIEWER.md.
/// </summary>
public sealed record DiffRow(
    string AccessibleName,
    string Display,
    string Marker,
    string LineNumber,
    bool IsHeader)
{
    public static DiffRow FromHeader(DiffHunk hunk, int index, int total) =>
        new(
            AccessibleName: hunk.AccessibleName(index, total),
            Display: hunk.Display,
            Marker: string.Empty,
            LineNumber: string.Empty,
            IsHeader: true);

    public static DiffRow FromLine(DiffLine line) =>
        new(
            AccessibleName: line.AccessibleName,
            // The raw content, not the spoken form: a sighted reader wants
            // the actual line, including when it is empty.
            Display: line.Content,
            Marker: line.Marker,
            LineNumber: line.LineNumberDisplay,
            IsHeader: false);

    /// <summary>Flatten a whole file diff into rows, headers included.</summary>
    public static IReadOnlyList<DiffRow> From(FileDiff diff)
    {
        var rows = new List<DiffRow>();

        for (var i = 0; i < diff.Hunks.Count; i++)
        {
            var hunk = diff.Hunks[i];
            rows.Add(FromHeader(hunk, i + 1, diff.Hunks.Count));
            rows.AddRange(hunk.Lines.Select(FromLine));
        }

        return rows;
    }
}
