using System.Globalization;
using GitApp.Domain;

namespace GitApp.Services;

/// <summary>
/// Parses unified diff output from git.
///
/// Line numbers are reconstructed as we walk, because a unified diff only
/// states them once per hunk in the @@ header and leaves the reader to count.
/// Getting that counting wrong is invisible in a rendered diff, where the
/// eye follows position on screen, and completely disorienting in speech,
/// where the number is the only positional information there is.
/// </summary>
public static class UnifiedDiffParser
{
    public static FileDiff Parse(string raw, string path)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return new FileDiff(path, Array.Empty<DiffHunk>());
        }

        // Git reports a binary file instead of a diff body.
        if (raw.Contains("\nBinary files ", StringComparison.Ordinal)
            || raw.StartsWith("Binary files ", StringComparison.Ordinal)
            || raw.Contains("GIT binary patch", StringComparison.Ordinal))
        {
            return new FileDiff(path, Array.Empty<DiffHunk>(), IsBinary: true);
        }

        var hunks = new List<DiffHunk>();
        string? oldPath = null;

        var lines = raw.Replace("\r\n", "\n").Split('\n');

        var current = new List<DiffLine>();
        var originalStart = 0;
        var modifiedStart = 0;
        var originalCount = 0;
        var modifiedCount = 0;
        var originalCursor = 0;
        var modifiedCursor = 0;
        var inHunk = false;

        void FlushHunk()
        {
            if (inHunk && current.Count > 0)
            {
                hunks.Add(new DiffHunk(
                    originalStart, originalCount, modifiedStart, modifiedCount, current.ToList()));
            }

            current.Clear();
        }

        foreach (var line in lines)
        {
            if (line.StartsWith("@@", StringComparison.Ordinal))
            {
                FlushHunk();

                if (!TryParseHunkHeader(line, out originalStart, out originalCount, out modifiedStart, out modifiedCount))
                {
                    inHunk = false;
                    continue;
                }

                originalCursor = originalStart;
                modifiedCursor = modifiedStart;
                inHunk = true;
                continue;
            }

            if (!inHunk)
            {
                // File headers, before the first hunk.
                if (line.StartsWith("--- a/", StringComparison.Ordinal))
                {
                    oldPath = line[6..];
                }
                else if (line.StartsWith("rename from ", StringComparison.Ordinal))
                {
                    oldPath = line[12..];
                }

                continue;
            }

            if (line.Length == 0)
            {
                // A completely empty line in the body is the trailing split
                // artefact, not a context line. A real blank context line
                // arrives as a single space.
                continue;
            }

            switch (line[0])
            {
                case '+':
                    current.Add(new DiffLine(DiffLineKind.Added, line[1..], null, modifiedCursor));
                    modifiedCursor++;
                    break;

                case '-':
                    current.Add(new DiffLine(DiffLineKind.Removed, line[1..], originalCursor, null));
                    originalCursor++;
                    break;

                case ' ':
                    current.Add(new DiffLine(DiffLineKind.Unchanged, line[1..], originalCursor, modifiedCursor));
                    originalCursor++;
                    modifiedCursor++;
                    break;

                case '\\':
                    // "\ No newline at end of file". Belongs to the line
                    // before it and advances neither cursor; dropping it
                    // would be a small lie, but announcing it on its own row
                    // is noise in the middle of a change.
                    break;

                default:
                    // "diff --git", "index", "new file mode", and anything
                    // else git emits between hunks.
                    break;
            }
        }

        FlushHunk();

        return new FileDiff(path, hunks, IsBinary: false, OldPath: oldPath);
    }

    /// <summary>
    /// Parse "@@ -154,12 +159,39 @@ optional heading".
    ///
    /// The counts are optional: "@@ -1 +1 @@" means one line on each side.
    /// Omitting that case makes single-line diffs vanish.
    /// </summary>
    internal static bool TryParseHunkHeader(
        string header,
        out int originalStart,
        out int originalCount,
        out int modifiedStart,
        out int modifiedCount)
    {
        originalStart = originalCount = modifiedStart = modifiedCount = 0;

        var close = header.IndexOf("@@", 2, StringComparison.Ordinal);
        if (close < 0)
        {
            return false;
        }

        var ranges = header[2..close].Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (ranges.Length < 2)
        {
            return false;
        }

        var original = ranges.FirstOrDefault(r => r.StartsWith('-'));
        var modified = ranges.FirstOrDefault(r => r.StartsWith('+'));

        return original is not null
            && modified is not null
            && TryParseRange(original[1..], out originalStart, out originalCount)
            && TryParseRange(modified[1..], out modifiedStart, out modifiedCount);
    }

    private static bool TryParseRange(string range, out int start, out int count)
    {
        start = 0;
        count = 1;

        var comma = range.IndexOf(',');
        if (comma < 0)
        {
            return int.TryParse(range, NumberStyles.Integer, CultureInfo.InvariantCulture, out start);
        }

        return int.TryParse(range[..comma], NumberStyles.Integer, CultureInfo.InvariantCulture, out start)
            && int.TryParse(range[(comma + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out count);
    }
}
