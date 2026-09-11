namespace GitApp.Domain;

/// <summary>
/// One line of a remote file, already carrying what a screen reader should
/// say for it.
///
/// Content first, line number last: leading with the number would open
/// every row with "line", and the listener would wait through it each time
/// to reach the part that differs. An empty line has to say "blank";
/// silence is indistinguishable from a row that failed to read. See
/// docs/REPOSITORY-VIEW.md.
/// </summary>
public sealed record FileLine(int Number, string Content)
{
    public string AccessibleName =>
        string.IsNullOrWhiteSpace(Content)
            ? $"blank, line {Number}"
            : $"{Content}, line {Number}";

    /// <summary>The gutter text shown beside the line, not spoken separately.</summary>
    public string NumberDisplay => Number.ToString();
}

/// <summary>
/// A file opened from the repository tree.
///
/// Loaded as a whole before the line list is filled, because partial data
/// arriving in waves re-renders the list and moves focus. See
/// ARCHITECTURE 4.3.
/// </summary>
public sealed record GitHubFile(
    string Name,
    string Path,
    string Branch,
    int ByteSize,
    bool IsBinary,
    bool TooLarge,
    string? Text,
    IReadOnlyList<FileLine> Lines,
    bool IsMarkdown,
    string HtmlUrl,
    string? LastSubject = null,
    DateTimeOffset? LastTouched = null)
{
    public bool ShowDocument => IsMarkdown && Text is not null && !IsBinary && !TooLarge;

    public bool ShowLines => !IsMarkdown && Text is not null && !IsBinary && !TooLarge;

    public bool ShowUnavailable => IsBinary || TooLarge;

    public string SizeWord => FormatSize(ByteSize);

    public string UnavailableMessage => IsBinary
        ? "This file is not text."
        : TooLarge
            ? "This file is too large to show here."
            : string.Empty;

    /// <summary>
    /// What the load announces when it finishes. Filename first: that is
    /// what the user asked to open. Size in words, never "KB", because a
    /// screen reader reads "KB" as letters.
    /// </summary>
    public string Summary
    {
        get
        {
            var parts = new List<string> { Name };

            if (IsBinary)
            {
                parts.Add("not text");
            }
            else if (TooLarge)
            {
                parts.Add("too large to show here");
            }
            else if (IsMarkdown)
            {
                parts.Add("rendered as markdown");
            }
            else if (Lines.Count == 0)
            {
                parts.Add("empty");
            }
            else
            {
                parts.Add(Lines.Count == 1 ? "1 line" : $"{Lines.Count} lines");
            }

            parts.Add(SizeWord);
            return string.Join(", ", parts);
        }
    }

    public IReadOnlyList<string> AboutFacts
    {
        get
        {
            var facts = new List<string> { Path, $"on {Branch}", SizeWord };

            if (IsBinary)
            {
                facts.Add("not text");
            }
            else if (TooLarge)
            {
                facts.Add("too large to show here");
            }
            else if (IsMarkdown)
            {
                facts.Add("rendered as markdown");
            }
            else if (Lines.Count == 0)
            {
                facts.Add("empty");
            }
            else
            {
                facts.Add(Lines.Count == 1 ? "1 line" : $"{Lines.Count} lines");
            }

            if (!string.IsNullOrWhiteSpace(LastSubject))
            {
                facts.Add(LastSubject.Trim());
            }

            if (LastTouched is { } when)
            {
                facts.Add(RelativeTime.From(when));
            }

            return facts;
        }
    }

    public static bool IsMarkdownName(string name)
    {
        var ext = System.IO.Path.GetExtension(name);
        return ext.Equals(".md", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".markdown", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".mdown", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".mdwn", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Words, not abbreviations. "18 kilobytes" rather than "18 KB": a
    /// screen reader reads "KB" as "kay bee".
    /// </summary>
    public static string FormatSize(int bytes)
    {
        if (bytes < 0)
        {
            bytes = 0;
        }

        if (bytes == 1)
        {
            return "1 byte";
        }

        if (bytes < 1000)
        {
            return $"{bytes} bytes";
        }

        if (bytes < 1_000_000)
        {
            var kilobytes = Math.Max(1, (int)Math.Round(bytes / 1000.0));
            return kilobytes == 1 ? "1 kilobyte" : $"{kilobytes} kilobytes";
        }

        var megabytes = Math.Max(1, (int)Math.Round(bytes / 1_000_000.0));
        return megabytes == 1 ? "1 megabyte" : $"{megabytes} megabytes";
    }

    /// <summary>
    /// Split on newlines the way an editor counts lines: a trailing newline
    /// does not invent an extra blank row.
    /// </summary>
    public static IReadOnlyList<FileLine> SplitLines(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return Array.Empty<FileLine>();
        }

        var raw = text.Replace("\r\n", "\n").Replace('\r', '\n');
        var parts = raw.Split('\n');
        if (parts[^1].Length == 0)
        {
            parts = parts[..^1];
        }

        var lines = new FileLine[parts.Length];
        for (var i = 0; i < parts.Length; i++)
        {
            lines[i] = new FileLine(i + 1, parts[i]);
        }

        return lines;
    }

    public static string BlobUrl(string repoHtmlUrl, string branch, string path)
    {
        var root = repoHtmlUrl.TrimEnd('/');
        var refName = string.IsNullOrWhiteSpace(branch) ? "HEAD" : branch;
        var relative = path.Replace('\\', '/').Trim('/');
        return string.IsNullOrEmpty(relative)
            ? $"{root}/blob/{refName}"
            : $"{root}/blob/{refName}/{relative}";
    }
}
