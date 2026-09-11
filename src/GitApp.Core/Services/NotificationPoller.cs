using GitApp.Domain;
using GitApp.GitHub;

namespace GitApp.Services;

/// <summary>
/// When to poll next, and which notifications are new enough to announce.
///
/// This is the "polling done correctly" of ARCHITECTURE 4.4, factored out of
/// the app so it is testable without a clock, a network, or a window. It holds
/// no <see cref="GitHubClient"/> and starts no timer: the caller asks
/// <see cref="IsDue"/>, does the request when it says so, and hands the result
/// back to <see cref="Observe"/> or <see cref="Backoff"/>. Time is passed in,
/// never read, so a test can drive a day of polling in a few lines.
///
/// Two things it owns that are easy to get wrong by hand:
///
/// - <b>The floor between polls.</b> GitHub's <c>X-Poll-Interval</c> is a
///   minimum, not a suggestion, and asking faster earns a secondary rate
///   limit. The next-due time is never sooner than the larger of that header
///   and this app's own floor.
/// - <b>What counts as new.</b> A toast is worth raising once, for a thread
///   that just arrived or just changed, and only while it is unread. Polling
///   returns the same unread threads every time until they are read; treating
///   every reply as new would toast the whole inbox on a schedule.
/// </summary>
public sealed class NotificationPoller
{
    private readonly int _minIntervalSeconds;
    private readonly Dictionary<string, DateTimeOffset> _seen = new(StringComparer.Ordinal);
    private int _rateLimitStreak;

    /// <param name="minIntervalSeconds">
    /// This app's own floor, applied when it is larger than GitHub's. Defaults
    /// to the same 60 seconds GitHub uses when it sends no header.
    /// </param>
    public NotificationPoller(int minIntervalSeconds = 60) =>
        _minIntervalSeconds = Math.Max(1, minIntervalSeconds);

    /// <summary>When the next poll becomes allowed. Starts in the past so the first is immediate.</summary>
    public DateTimeOffset NextPollAt { get; private set; } = DateTimeOffset.MinValue;

    /// <summary>The seconds this poller last waited, after applying the floor and any backoff.</summary>
    public int LastIntervalSeconds { get; private set; }

    /// <summary>The opaque token to send as the next conditional request's If-Modified-Since.</summary>
    public string? PollToken { get; private set; }

    /// <summary>Whether a poll is allowed yet.</summary>
    public bool IsDue(DateTimeOffset now) => now >= NextPollAt;

    /// <summary>
    /// Record a successful poll, advance the schedule by the honoured interval,
    /// and return the notifications worth announcing: those newly arrived or
    /// newly changed, and still unread. A 304 (nothing changed) returns none.
    /// </summary>
    public IReadOnlyList<GitHubNotification> Observe(NotificationPage page, DateTimeOffset now)
    {
        _rateLimitStreak = 0;
        PollToken = page.PollToken ?? PollToken;

        var interval = page.PollIntervalSeconds > 0
            ? Math.Max(_minIntervalSeconds, page.PollIntervalSeconds)
            : _minIntervalSeconds;
        LastIntervalSeconds = interval;
        NextPollAt = now + TimeSpan.FromSeconds(interval);

        if (page.NotModified)
        {
            return Array.Empty<GitHubNotification>();
        }

        var fresh = new List<GitHubNotification>();
        foreach (var notification in page.Items)
        {
            var isNew = !_seen.TryGetValue(notification.Id, out var previous)
                || notification.UpdatedAt > previous;

            if (isNew && notification.Unread)
            {
                fresh.Add(notification);
            }

            _seen[notification.Id] = notification.UpdatedAt;
        }

        return fresh;
    }

    /// <summary>
    /// Record a failed poll and back the schedule off. A rate limit doubles the
    /// wait on each consecutive hit, up to an hour, because leaning on a
    /// secondary rate limit only extends it. Anything else — offline, a 5xx —
    /// simply waits the normal floor and tries again.
    /// </summary>
    public void Backoff(DateTimeOffset now, GitHubFailure failure)
    {
        int seconds;
        if (failure == GitHubFailure.RateLimited)
        {
            _rateLimitStreak++;
            var factor = 1L << Math.Min(_rateLimitStreak, 16);
            seconds = (int)Math.Min(3600, _minIntervalSeconds * factor);
        }
        else
        {
            seconds = _minIntervalSeconds;
        }

        LastIntervalSeconds = seconds;
        NextPollAt = now + TimeSpan.FromSeconds(seconds);
    }
}
