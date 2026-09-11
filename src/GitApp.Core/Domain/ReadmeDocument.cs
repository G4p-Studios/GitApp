using System.Text;
using System.Text.RegularExpressions;

namespace GitApp.Domain;

public enum ReadmeBlockKind
{
    Heading,
    Paragraph,
    List,
    Code,
}

/// <summary>
/// One block of a rendered README.
///
/// The README is a document, not a list: headings must expose a heading
/// level so a screen reader can jump between them, and that is the whole
/// reason for parsing Markdown rather than showing it raw. See
/// docs/REPOSITORY-VIEW.md.
/// </summary>
public sealed record ReadmeBlock(
    ReadmeBlockKind Kind,
    string Text,
    int Level = 0,
    string? Language = null,
    IReadOnlyList<ReadmeLink>? Links = null)
{
    public IReadOnlyList<ReadmeLink> Links { get; } = Links ?? Array.Empty<ReadmeLink>();

    /// <summary>
    /// What a screen reader should say for this block.
    ///
    /// Code is summarised first so the listener knows a block of source is
    /// coming, then the code itself as one string. Rendering each character
    /// as its own element would spell punctuation aloud one mark at a time.
    /// </summary>
    public string AccessibleName
    {
        get
        {
            if (Kind != ReadmeBlockKind.Code)
            {
                return Text;
            }

            var lines = CountLines(Text);
            var size = lines == 1 ? "1 line" : $"{lines} lines";

            return string.IsNullOrWhiteSpace(Language)
                ? $"Code block, {size}. {Text}"
                : $"Code block, {Language}, {size}. {Text}";
        }
    }

    private static int CountLines(string text)
    {
        if (text.Length == 0)
        {
            return 0;
        }

        var n = 1;
        foreach (var c in text)
        {
            if (c == '\n')
            {
                n++;
            }
        }

        return n;
    }
}

/// <summary>
/// A Markdown link lifted out of a block so it can be a real control.
///
/// MAUI has no portable hyperlink, so the view renders these as buttons.
/// NVDA's link list will not find them; the button list will. That is the
/// closest portable answer, and it is still better than leaving the URL
/// as characters inside a paragraph.
/// </summary>
public sealed record ReadmeLink(string Text, string Url)
{
    public string AccessibleName => $"{Text}, link";
}

/// <summary>
/// A README turned into blocks a native view can render.
///
/// This is not CommonMark. It covers the shapes that actually appear in
/// repository readmes and that carry accessibility meaning: headings,
/// fenced code, lists, paragraphs, and links. Everything else is left as
/// text rather than guessed into the wrong role.
/// </summary>
public static partial class ReadmeDocument
{
    public static IReadOnlyList<ReadmeBlock> Parse(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return Array.Empty<ReadmeBlock>();
        }

        var lines = markdown.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var blocks = new List<ReadmeBlock>();
        var i = 0;

        while (i < lines.Length)
        {
            var line = lines[i];

            if (string.IsNullOrWhiteSpace(line))
            {
                i++;
                continue;
            }

            if (line.StartsWith("```", StringComparison.Ordinal))
            {
                i = ReadFence(lines, i, blocks);
                continue;
            }

            if (Heading().Match(line) is { Success: true } heading)
            {
                var (text, links) = Inline(heading.Groups[2].Value);
                blocks.Add(new ReadmeBlock(
                    ReadmeBlockKind.Heading,
                    text,
                    heading.Groups[1].Value.Length,
                    Links: links));
                i++;
                continue;
            }

            if (ListItem().IsMatch(line))
            {
                i = ReadList(lines, i, blocks);
                continue;
            }

            i = ReadParagraph(lines, i, blocks);
        }

        return blocks;
    }

    private static int ReadFence(string[] lines, int start, List<ReadmeBlock> blocks)
    {
        var opener = lines[start];
        var fence = 0;
        while (fence < opener.Length && opener[fence] == '`')
        {
            fence++;
        }

        var language = opener[fence..].Trim();
        if (language.Length == 0)
        {
            language = null;
        }

        var body = new StringBuilder();
        var i = start + 1;

        for (; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.StartsWith(new string('`', fence), StringComparison.Ordinal)
                && line.TrimStart('`').Trim().Length == 0)
            {
                i++;
                break;
            }

            if (body.Length > 0)
            {
                body.Append('\n');
            }

            body.Append(line);
        }

        blocks.Add(new ReadmeBlock(
            ReadmeBlockKind.Code,
            body.ToString(),
            Language: language));

        return i;
    }

    private static int ReadList(string[] lines, int start, List<ReadmeBlock> blocks)
    {
        var items = new List<string>();
        var links = new List<ReadmeLink>();
        var i = start;

        while (i < lines.Length && ListItem().Match(lines[i]) is { Success: true } match)
        {
            var (text, itemLinks) = Inline(match.Groups[1].Value);
            items.Add(text);
            links.AddRange(itemLinks);
            i++;
        }

        blocks.Add(new ReadmeBlock(
            ReadmeBlockKind.List,
            string.Join("\n", items),
            Links: links));

        return i;
    }

    private static int ReadParagraph(string[] lines, int start, List<ReadmeBlock> blocks)
    {
        var body = new StringBuilder();
        var i = start;

        while (i < lines.Length)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line)
                || line.StartsWith("```", StringComparison.Ordinal)
                || Heading().IsMatch(line)
                || ListItem().IsMatch(line))
            {
                break;
            }

            var stripped = line.StartsWith('>')
                ? line.TrimStart('>').TrimStart()
                : line.Trim();

            if (body.Length > 0)
            {
                body.Append(' ');
            }

            body.Append(stripped);
            i++;
        }

        var (text, links) = Inline(body.ToString());
        if (text.Length > 0)
        {
            blocks.Add(new ReadmeBlock(ReadmeBlockKind.Paragraph, text, Links: links));
        }

        return i;
    }

    /// <summary>
    /// Links become their visible text in the paragraph, and are also
    /// returned separately so the view can make them activatable.
    /// Images become their alt text. Emphasis markers are stripped so
    /// they are not read as punctuation around every stressed word.
    /// </summary>
    internal static (string Text, IReadOnlyList<ReadmeLink> Links) Inline(string raw)
    {
        var links = new List<ReadmeLink>();

        var withoutImages = Image().Replace(raw, m =>
            string.IsNullOrEmpty(m.Groups[1].Value) ? "image" : m.Groups[1].Value);

        var withoutLinks = Link().Replace(withoutImages, m =>
        {
            var text = m.Groups[1].Value;
            var url = m.Groups[2].Value;
            if (!string.IsNullOrWhiteSpace(url))
            {
                links.Add(new ReadmeLink(
                    string.IsNullOrEmpty(text) ? url : text,
                    url));
            }

            return text;
        });

        var cleaned = Emphasis().Replace(withoutLinks, m =>
            m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value);
        cleaned = InlineCode().Replace(cleaned, "$1");
        cleaned = Whitespace().Replace(cleaned, " ").Trim();

        return (cleaned, links);
    }

    [GeneratedRegex(@"^(#{1,6})\s+(.*)$")]
    private static partial Regex Heading();

    [GeneratedRegex(@"^\s*(?:[-*+]|\d+\.)\s+(.*)$")]
    private static partial Regex ListItem();

    [GeneratedRegex(@"!\[([^\]]*)\]\([^)]+\)")]
    private static partial Regex Image();

    [GeneratedRegex(@"\[([^\]]*)\]\(([^)]+)\)")]
    private static partial Regex Link();

    [GeneratedRegex(@"\*{1,2}([^*]+)\*{1,2}|_{1,2}([^_]+)_{1,2}")]
    private static partial Regex Emphasis();

    [GeneratedRegex(@"`([^`]+)`")]
    private static partial Regex InlineCode();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
