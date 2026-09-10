using GitApp.Domain;
using GitApp.Services;

namespace GitApp.Core.Tests;

/// <summary>
/// Parsing unified diffs, and the announcements built from them.
///
/// Line numbers are the thing under test. A unified diff states them once
/// per hunk and leaves the reader to count; miscounting is invisible in a
/// rendered diff, where the eye follows position on screen, and completely
/// disorienting in speech, where the number is the only position there is.
/// </summary>
public class DiffParsingTests
{
    private static string Diff(params string[] lines) => string.Join("\n", lines);

    [Fact]
    public void CountsLineNumbersThroughAMixedHunk()
    {
        var raw = Diff(
            "diff --git a/src/Thing.cs b/src/Thing.cs",
            "index 1234567..89abcde 100644",
            "--- a/src/Thing.cs",
            "+++ b/src/Thing.cs",
            "@@ -10,6 +10,7 @@ public class Thing",
            " first context",
            "-removed one",
            "+added one",
            "+added two",
            " second context",
            " third context");

        var diff = UnifiedDiffParser.Parse(raw, "src/Thing.cs");
        var hunk = Assert.Single(diff.Hunks);

        // Original side: 10 context, 11 removed, then context resumes at 12.
        // Modified side: 10 context, 11 and 12 added, context resumes at 13.
        Assert.Collection(
            hunk.Lines,
            l => AssertLine(l, DiffLineKind.Unchanged, "first context", 10, 10),
            l => AssertLine(l, DiffLineKind.Removed, "removed one", 11, null),
            l => AssertLine(l, DiffLineKind.Added, "added one", null, 11),
            l => AssertLine(l, DiffLineKind.Added, "added two", null, 12),
            l => AssertLine(l, DiffLineKind.Unchanged, "second context", 12, 13),
            l => AssertLine(l, DiffLineKind.Unchanged, "third context", 13, 14));
    }

    [Fact]
    public void HandlesHunkHeadersWithoutCounts()
    {
        // "@@ -1 +1 @@" means exactly one line each side. Treating the
        // missing count as zero makes single-line diffs disappear.
        var raw = Diff(
            "@@ -1 +1 @@",
            "-old",
            "+new");

        var diff = UnifiedDiffParser.Parse(raw, "f.txt");
        var hunk = Assert.Single(diff.Hunks);

        Assert.Equal(1, hunk.OriginalStart);
        Assert.Equal(1, hunk.OriginalCount);
        Assert.Equal(1, hunk.ModifiedStart);
        Assert.Equal(1, hunk.ModifiedCount);
        Assert.Equal(2, hunk.Lines.Count);
    }

    [Fact]
    public void ReadsMultipleHunks()
    {
        var raw = Diff(
            "--- a/f.txt",
            "+++ b/f.txt",
            "@@ -1,3 +1,3 @@",
            " a",
            "-b",
            "+B",
            "@@ -20,3 +20,4 @@",
            " x",
            "+y",
            " z");

        var diff = UnifiedDiffParser.Parse(raw, "f.txt");

        Assert.Equal(2, diff.Hunks.Count);
        Assert.Equal(20, diff.Hunks[1].OriginalStart);
        Assert.Equal(2, diff.AddedCount);
        Assert.Equal(1, diff.RemovedCount);
    }

    [Fact]
    public void DoesNotTreatTheNoNewlineMarkerAsALine()
    {
        // "\ No newline at end of file" belongs to the line before it and
        // advances neither cursor. Counting it would shift every subsequent
        // line number.
        var raw = Diff(
            "@@ -1,2 +1,2 @@",
            " kept",
            "-old",
            "\\ No newline at end of file",
            "+new",
            "\\ No newline at end of file");

        var diff = UnifiedDiffParser.Parse(raw, "f.txt");
        var hunk = Assert.Single(diff.Hunks);

        Assert.Equal(3, hunk.Lines.Count);
        Assert.Equal(DiffLineKind.Removed, hunk.Lines[1].Kind);
        Assert.Equal(DiffLineKind.Added, hunk.Lines[2].Kind);
    }

    [Fact]
    public void KeepsLinesThatBeginWithAPlusOrMinusInTheirContent()
    {
        // The first character is the marker; everything after it is content,
        // including another marker character. Trimming both would corrupt
        // diffs of diffs, and of any code using leading operators.
        var raw = Diff(
            "@@ -1,2 +1,2 @@",
            "--removed starting with dash",
            "++added starting with plus");

        var diff = UnifiedDiffParser.Parse(raw, "f.txt");
        var hunk = Assert.Single(diff.Hunks);

        Assert.Equal("-removed starting with dash", hunk.Lines[0].Content);
        Assert.Equal("+added starting with plus", hunk.Lines[1].Content);
    }

    [Fact]
    public void DetectsBinaryFiles()
    {
        var raw = "diff --git a/logo.png b/logo.png\nindex 1234..5678 100644\nBinary files a/logo.png and b/logo.png differ\n";

        var diff = UnifiedDiffParser.Parse(raw, "logo.png");

        Assert.True(diff.IsBinary);
        Assert.Empty(diff.Hunks);
        Assert.Contains("binary file", diff.Summary);
        Assert.Contains("no text diff available", diff.Summary);
    }

    [Fact]
    public void EmptyDiffIsNotAnError()
    {
        var diff = UnifiedDiffParser.Parse(string.Empty, "f.txt");

        Assert.False(diff.HasChanges);
        Assert.Contains("no changes", diff.Summary);
    }

    // -----------------------------------------------------------------
    // Announcements
    // -----------------------------------------------------------------

    [Fact]
    public void AnnouncesAddedAndRemovedAsWordsNotSymbols()
    {
        // Most screen readers do not speak "+" or "-" at default punctuation
        // levels, so the symbol cannot be what carries the distinction.
        var added = new DiffLine(DiffLineKind.Added, "var x = 1;", null, 15);
        var removed = new DiffLine(DiffLineKind.Removed, "var x = 0;", 12, null);

        Assert.Equal("var x = 1;, added, modified line 15", added.AccessibleName);
        Assert.Equal("var x = 0;, removed, original line 12", removed.AccessibleName);
    }

    [Fact]
    public void AnnouncesBothNumbersWhenAnUnchangedLineHasMoved()
    {
        var moved = new DiffLine(DiffLineKind.Unchanged, "same text", 12, 15);
        var still = new DiffLine(DiffLineKind.Unchanged, "same text", 12, 12);

        Assert.Equal("same text, original line 12, modified line 15", moved.AccessibleName);
        Assert.Equal("same text, unchanged line 12", still.AccessibleName);
    }

    [Fact]
    public void SaysBlankRatherThanNothingForAnEmptyLine()
    {
        // Silence is indistinguishable from a row that failed to read.
        var empty = new DiffLine(DiffLineKind.Added, string.Empty, null, 7);
        var whitespace = new DiffLine(DiffLineKind.Unchanged, "   ", 3, 3);

        Assert.Equal("blank, added, modified line 7", empty.AccessibleName);
        Assert.Equal("blank, unchanged line 3", whitespace.AccessibleName);
    }

    [Fact]
    public void HunkHeaderLeadsWithPositionInTheWhole()
    {
        var hunk = new DiffHunk(154, 12, 159, 39, Array.Empty<DiffLine>());

        Assert.Equal(
            "Difference 2 of 5, original line 154, 12 lines changed, modified line 159, 39 lines changed",
            hunk.AccessibleName(2, 5));
    }

    [Fact]
    public void PluralisesProperly()
    {
        var one = new DiffHunk(1, 1, 1, 0, Array.Empty<DiffLine>());

        var spoken = one.AccessibleName(1, 1);

        Assert.Contains("1 line changed", spoken);
        Assert.Contains("no lines changed", spoken);
        Assert.DoesNotContain("1 lines changed", spoken);
    }

    [Fact]
    public void SummaryStatesTheSizeBeforeYouStartMovingThroughIt()
    {
        var raw = Diff(
            "@@ -1,3 +1,4 @@",
            " a",
            "-b",
            "+B",
            "+c",
            " d");

        var diff = UnifiedDiffParser.Parse(raw, "src/Thing.cs");

        Assert.Equal("Thing.cs, 1 difference, 2 lines added, 1 line removed", diff.Summary);
    }

    private static void AssertLine(DiffLine line, DiffLineKind kind, string content, int? original, int? modified)
    {
        Assert.Equal(kind, line.Kind);
        Assert.Equal(content, line.Content);
        Assert.Equal(original, line.OriginalLineNumber);
        Assert.Equal(modified, line.ModifiedLineNumber);
    }
}
