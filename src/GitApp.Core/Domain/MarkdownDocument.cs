using System.Text;

namespace GitApp.Domain;

public enum MarkdownRangeKind
{
    Heading,
    Code,
    Link,
}

/// <summary>
/// A formatted run inside <see cref="MarkdownDocumentLayout.Text"/>.
/// Offsets count the same <c>\\r</c> paragraph marks the document control uses.
/// </summary>
public sealed record MarkdownRange(
    int Start,
    int Length,
    MarkdownRangeKind Kind,
    int Level = 0,
    string? Url = null);

/// <summary>
/// The README (or any Markdown body) as one document: the text you arrow
/// through, and the ranges that make headings, code, and links real.
/// </summary>
public sealed record MarkdownDocumentLayout(
    string Text,
    IReadOnlyList<MarkdownRange> Ranges);

/// <summary>
/// Flatten parsed Markdown blocks into one document.
///
/// A stack of labels is not a document: arrow keys cannot move a caret,
/// and a screen reader cannot read it line by line. Links that have been
/// pulled out as buttons sit in the wrong role and after the sentence they
/// belong to. This layout is what the native document control renders.
/// See docs/REPOSITORY-VIEW.md.
/// </summary>
public static class MarkdownDocument
{
    public static MarkdownDocumentLayout Layout(
        IEnumerable<ReadmeBlock>? blocks,
        string? title = null,
        int headingOffset = 0)
    {
        var text = new StringBuilder();
        var ranges = new List<MarkdownRange>();

        if (!string.IsNullOrWhiteSpace(title))
        {
            AppendHeading(text, ranges, title.Trim(), 1);
        }

        if (blocks is not null)
        {
            foreach (var block in blocks)
            {
                if (text.Length > 0)
                {
                    text.Append('\r');
                    text.Append('\r');
                }

                switch (block.Kind)
                {
                    case ReadmeBlockKind.Heading:
                        AppendHeading(
                            text,
                            ranges,
                            block.Spans,
                            Math.Clamp(block.Level + headingOffset, 1, 6));
                        break;

                    case ReadmeBlockKind.Code:
                        AppendCode(text, ranges, block);
                        break;

                    case ReadmeBlockKind.List:
                        AppendList(text, ranges, block.Spans);
                        break;

                    default:
                        AppendSpans(text, ranges, block.Spans);
                        break;
                }
            }
        }

        return new MarkdownDocumentLayout(text.ToString(), ranges);
    }

    /// <summary>
    /// Turn a Markdown destination into something a browser can open.
    /// Relative paths resolve against the file or repository they came from.
    /// </summary>
    public static string ResolveUrl(string url, string? baseUri)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return url;
        }

        if (Uri.TryCreate(url, UriKind.Absolute, out var absolute))
        {
            return absolute.ToString();
        }

        if (Uri.TryCreate(baseUri, UriKind.Absolute, out var root)
            && Uri.TryCreate(root, url, out var combined))
        {
            return combined.ToString();
        }

        return url;
    }

    private static void AppendHeading(
        StringBuilder text, List<MarkdownRange> ranges, string heading, int level)
    {
        var start = text.Length;
        text.Append(heading);
        ranges.Add(new MarkdownRange(start, heading.Length, MarkdownRangeKind.Heading, level));
    }

    private static void AppendHeading(
        StringBuilder text,
        List<MarkdownRange> ranges,
        IReadOnlyList<ReadmeSpan> spans,
        int level)
    {
        var start = text.Length;
        AppendSpans(text, ranges, spans);
        ranges.Add(new MarkdownRange(start, text.Length - start, MarkdownRangeKind.Heading, level));
    }

    private static void AppendCode(
        StringBuilder text, List<MarkdownRange> ranges, ReadmeBlock block)
    {
        var start = text.Length;
        text.Append(block.CodeSummary);
        text.Append('\r');
        text.Append(block.Text.Replace("\r\n", "\n").Replace('\n', '\r'));
        ranges.Add(new MarkdownRange(start, text.Length - start, MarkdownRangeKind.Code));
    }

    private static void AppendList(
        StringBuilder text, List<MarkdownRange> ranges, IReadOnlyList<ReadmeSpan> spans)
    {
        text.Append("• ");
        foreach (var span in spans)
        {
            if (!span.IsLink && span.Text == "\n")
            {
                text.Append('\r');
                text.Append("• ");
                continue;
            }

            AppendSpan(text, ranges, span);
        }
    }

    private static void AppendSpans(
        StringBuilder text, List<MarkdownRange> ranges, IReadOnlyList<ReadmeSpan> spans)
    {
        foreach (var span in spans)
        {
            AppendSpan(text, ranges, span);
        }
    }

    private static void AppendSpan(
        StringBuilder text, List<MarkdownRange> ranges, ReadmeSpan span)
    {
        var piece = span.Text.Replace("\r\n", "\n").Replace('\n', '\r');
        if (piece.Length == 0)
        {
            return;
        }

        var start = text.Length;
        text.Append(piece);
        if (span.IsLink)
        {
            ranges.Add(new MarkdownRange(
                start, piece.Length, MarkdownRangeKind.Link, Url: span.Url));
        }
    }
}
