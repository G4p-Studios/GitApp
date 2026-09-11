using System.Text.RegularExpressions;

namespace GitApp.Services;

/// <summary>
/// Strips anything that looks like a credential out of text on its way to
/// the user, a log, or a bug report.
///
/// The rule in ARCHITECTURE 4.5 is that a token never appears in a message.
/// Keeping that rule by inspection does not scale: the risky paths are the
/// generic ones, where some exception nobody anticipated is formatted and
/// announced. So the text is filtered at the point it becomes visible,
/// rather than trusting every caller to have thought about it.
///
/// Deliberately over-eager. Redacting something harmless costs the user a
/// slightly vaguer error message; missing a real token reads it out loud.
/// </summary>
public static partial class Redaction
{
    public static string Apply(string? text) =>
        string.IsNullOrEmpty(text) ? string.Empty : TokenPattern().Replace(text, "[redacted]");

    /// <summary>
    /// GitHub's token prefixes, plus the generic "long opaque string after
    /// a colon in a URL" shape that basic auth uses.
    /// </summary>
    [GeneratedRegex(
        @"\b(gh[pousr]_[A-Za-z0-9]{16,}|github_pat_[A-Za-z0-9_]{20,})\b" +
        @"|(?<=://)[^/\s:@]+:[^/\s@]+(?=@)",
        RegexOptions.None)]
    private static partial Regex TokenPattern();
}
