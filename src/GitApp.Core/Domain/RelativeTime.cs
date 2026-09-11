namespace GitApp.Domain;

/// <summary>
/// "5 hours ago", the way git and github.com both say it.
///
/// Git hands us this already formatted for local history (%ar), but the
/// GitHub API sends ISO timestamps, and a list that mixes "5 hours ago" with
/// "2026-09-11T04:12:00Z" is jarring to read and worse to listen to. So
/// remote times are formatted to match.
///
/// Words, not abbreviations: "5 hours ago" rather than "5h". A screen reader
/// reads "5h" as "five aitch".
///
/// Elapsed time throughout, so no "yesterday". Hours run to 36 before days
/// begin, which would have made "yesterday" describe something closer to
/// two days old, and a listener cannot see the date to correct it.
/// </summary>
public static class RelativeTime
{
    public static string From(DateTimeOffset moment, DateTimeOffset? now = null)
    {
        var span = (now ?? DateTimeOffset.UtcNow) - moment;

        if (span < TimeSpan.Zero)
        {
            // Clock skew between us and the server. Saying "in 3 seconds"
            // about a commit that already exists is worse than rounding.
            span = TimeSpan.Zero;
        }

        var seconds = (long)span.TotalSeconds;

        return seconds switch
        {
            < 45 => "just now",
            < 90 => "1 minute ago",
            < 45 * 60 => Plural(seconds / 60, "minute"),
            < 90 * 60 => "1 hour ago",
            < 36 * 3600 => Plural(seconds / 3600, "hour"),
            < 60 * 86400 => Plural(seconds / 86400, "day"),
            < 365 * 86400 => Plural((long)Math.Round(seconds / (30.44 * 86400)), "month"),
            _ => Plural((long)Math.Round(seconds / (365.25 * 86400)), "year"),
        };
    }

    private static string Plural(long count, string unit) =>
        count == 1 ? $"1 {unit} ago" : $"{count} {unit}s ago";
}
