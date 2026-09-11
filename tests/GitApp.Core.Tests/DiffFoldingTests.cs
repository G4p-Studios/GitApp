using GitApp.Domain;

namespace GitApp.Core.Tests;

/// <summary>
/// Folding is the difference between a 400-line hunk being navigable and
/// being a wall, so the row list it produces has to be exactly right: the
/// header always present, the positions never renumbered by what is hidden,
/// and the state said as a word.
/// </summary>
public class DiffFoldingTests
{
    private static DiffHunk Hunk(int lineCount, int originalStart = 1, int modifiedStart = 1)
    {
        var lines = Enumerable.Range(0, lineCount)
            .Select(i => new DiffLine(
                DiffLineKind.Added, $"line {i}", null, modifiedStart + i))
            .ToList();

        return new DiffHunk(originalStart, 0, modifiedStart, lineCount, lines);
    }

    private static FileDiff Diff(params DiffHunk[] hunks) => new("a.cs", hunks);

    [Fact]
    public void SmallHunkIsNotFoldable()
    {
        var rows = DiffRow.Build(Diff(Hunk(5)), largeHunkLines: 40);

        Assert.Equal(6, rows.Count);
        Assert.False(rows[0].IsExpandable);

        // No state word at all, because there is no state to be in.
        Assert.DoesNotContain("collapsed", rows[0].AccessibleName);
        Assert.DoesNotContain("expanded", rows[0].AccessibleName);
    }

    [Fact]
    public void HunkAtTheThresholdStaysOpen()
    {
        // Strictly greater, so a threshold of 40 shows 40 lines.
        var rows = DiffRow.Build(Diff(Hunk(40)), largeHunkLines: 40);

        Assert.Equal(41, rows.Count);
        Assert.False(rows[0].IsExpandable);
    }

    [Fact]
    public void LargeHunkFoldsToItsHeaderAndSaysSo()
    {
        var rows = DiffRow.Build(Diff(Hunk(41)), largeHunkLines: 40);

        var header = Assert.Single(rows);
        Assert.True(header.IsExpandable);
        Assert.False(header.IsExpanded);
        Assert.Contains("Difference 1 of 1, collapsed,", header.AccessibleName);
    }

    [Fact]
    public void ExpandingRevealsTheLinesAndFlipsTheWord()
    {
        var rows = DiffRow.Build(Diff(Hunk(41)), new HashSet<int> { 0 }, largeHunkLines: 40);

        Assert.Equal(42, rows.Count);
        Assert.True(rows[0].IsExpanded);
        Assert.Contains("Difference 1 of 1, expanded,", rows[0].AccessibleName);
    }

    [Fact]
    public void FoldedHunksDoNotRenumberTheDifferences()
    {
        // The middle hunk is hidden. "Difference 3 of 3" must still be the
        // third difference in the file, not the second visible header.
        var rows = DiffRow.Build(
            Diff(Hunk(2), Hunk(50), Hunk(2)), largeHunkLines: 40);

        var headers = rows.Where(r => r.IsHeader).ToList();

        Assert.Equal(3, headers.Count);
        Assert.Contains("Difference 1 of 3", headers[0].AccessibleName);
        Assert.Contains("Difference 2 of 3", headers[1].AccessibleName);
        Assert.Contains("Difference 3 of 3", headers[2].AccessibleName);
    }

    [Fact]
    public void HeaderCarriesTheIndexOfItsHunk()
    {
        var rows = DiffRow.Build(
            Diff(Hunk(2), Hunk(50), Hunk(2)), largeHunkLines: 40);

        var headers = rows.Where(r => r.IsHeader).ToList();

        Assert.Equal(new[] { 0, 1, 2 }, headers.Select(h => h.HunkIndex));
    }

    [Fact]
    public void FoldedCountIsWhatTheOpeningSummaryReports()
    {
        var diff = Diff(Hunk(2), Hunk(50), Hunk(80));

        Assert.Equal(2, DiffRow.CountFolded(diff, largeHunkLines: 40));
        Assert.Equal(1, DiffRow.CountFolded(diff, new HashSet<int> { 1 }, 40));
        Assert.Equal(0, DiffRow.CountFolded(diff, new HashSet<int> { 1, 2 }, 40));
    }

    [Fact]
    public void FoldedHeaderSaysHowBigItIs()
    {
        var rows = DiffRow.Build(Diff(Hunk(41)), largeHunkLines: 40);

        // Visually: the line count is the only clue to what is hidden.
        Assert.Contains("41 lines", rows[0].Display);
    }

    [Fact]
    public void ExpandedStateOfOneHunkDoesNotLeakToAnother()
    {
        var rows = DiffRow.Build(
            Diff(Hunk(50), Hunk(50)), new HashSet<int> { 1 }, largeHunkLines: 40);

        var headers = rows.Where(r => r.IsHeader).ToList();

        Assert.False(headers[0].IsExpanded);
        Assert.True(headers[1].IsExpanded);
        Assert.Equal(52, rows.Count);
    }
}
