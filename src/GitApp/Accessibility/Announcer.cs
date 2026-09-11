using System.Diagnostics;

namespace GitApp.Accessibility;

public enum Urgency
{
    Polite,
    Assertive,
}

/// <summary>
/// Screen reader announcements.
///
/// Two channels, deliberately distinct:
///
///   Announce()  discrete events, spoken once. "Pushed 3 commits to origin/main".
///   SetStatus() text in the status line, for progress that updates in place.
///
/// Both are rate limited. An unthrottled announcer is worse than none: a Git
/// operation emitting progress per object floods the speech queue and buries
/// the message the user needs, and NVDA has no way to skip ahead.
///
/// This is one of the few pieces of the React Native build that survived the
/// port unchanged in substance. MAUI supplies the plumbing
/// (SemanticScreenReader) but no policy, and the policy was the hard part.
///
/// See docs/ARCHITECTURE.md section 3.5.
/// </summary>
public sealed class Announcer
{
    /// <summary>Minimum gap between two spoken announcements.</summary>
    private static readonly TimeSpan Throttle = TimeSpan.FromMilliseconds(500);

    public static Announcer Current { get; } = new();

    private readonly object _gate = new();
    private readonly Stopwatch _sinceLastSpoken = Stopwatch.StartNew();
    private string? _lastMessage;
    private string? _pending;
    private Urgency _pendingUrgency;
    private CancellationTokenSource? _flush;

    private string _status = string.Empty;

    /// <summary>Raised when the status line text changes.</summary>
    public event EventHandler<string>? StatusChanged;

    /// <summary>Test seam, so a fake can capture what would have been spoken.</summary>
    public Action<string> Speak { get; set; } = text => SemanticScreenReader.Announce(text);

    /// <summary>
    /// Speak a discrete message.
    ///
    /// Assertive messages jump the queue and are never coalesced away, because
    /// they are errors that stop the user's task. Polite messages arriving
    /// inside the throttle window replace any other polite message still
    /// waiting, which is what makes progress reporting safe: the user hears
    /// the latest state rather than a backlog of stale ones.
    /// </summary>
    public void Announce(string message, Urgency urgency = Urgency.Polite)
    {
        var text = message?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        lock (_gate)
        {
            // Identical consecutive messages are almost always a re-render,
            // not new information. Dropping them is the difference between a
            // usable app and one that repeats itself.
            if (text == _lastMessage && urgency != Urgency.Assertive)
            {
                return;
            }

            if (urgency == Urgency.Assertive)
            {
                SpeakNow(text);
                return;
            }

            if (_sinceLastSpoken.Elapsed >= Throttle && _flush is null)
            {
                SpeakNow(text);
                return;
            }

            _pending = text;
            _pendingUrgency = urgency;
            ScheduleFlush(Throttle - _sinceLastSpoken.Elapsed);
        }
    }

    /// <summary>
    /// Set the status line text. Unlike Announce, repeated identical values
    /// cost nothing, so callers may set this as often as they like.
    /// </summary>
    public void SetStatus(string text)
    {
        if (text == _status)
        {
            return;
        }

        _status = text;
        StatusChanged?.Invoke(this, text);
    }

    public void ClearStatus() => SetStatus(string.Empty);

    public string Status => _status;

    /// <summary>
    /// Announce the start of a long operation and hand back a completion
    /// callback. Silence during a long Git operation reads as a hang, so this
    /// pairing is mandatory for anything that can outlast a second.
    /// </summary>
    public Action<string> Operation(string startMessage)
    {
        Announce(startMessage);

        var settled = 0;
        return endMessage =>
        {
            // A retry must not double-announce.
            if (Interlocked.Exchange(ref settled, 1) == 1)
            {
                return;
            }

            ClearStatus();
            Announce(endMessage);
        };
    }

    /// <summary>Test seam: drop queued state between cases.</summary>
    public void Reset()
    {
        lock (_gate)
        {
            _flush?.Cancel();
            _flush = null;
            _pending = null;
            _lastMessage = null;
            _status = string.Empty;
            _sinceLastSpoken.Restart();
        }
    }

    private void ScheduleFlush(TimeSpan delay)
    {
        if (_flush is not null)
        {
            return;
        }

        var cts = new CancellationTokenSource();
        _flush = cts;

        _ = Task.Delay(delay < TimeSpan.Zero ? TimeSpan.Zero : delay, cts.Token)
            .ContinueWith(
                _ =>
                {
                    lock (_gate)
                    {
                        _flush = null;
                        var next = _pending;
                        _pending = null;
                        if (next is not null)
                        {
                            SpeakNow(next);
                        }
                    }
                },
                cts.Token,
                TaskContinuationOptions.OnlyOnRanToCompletion,
                TaskScheduler.Default);
    }

    private void SpeakNow(string text)
    {
        _lastMessage = text;
        _sinceLastSpoken.Restart();

        // Always on the UI thread.
        //
        // The throttle flushes from a timer continuation, which runs on the
        // thread pool, and the platform announcement needs the UI thread.
        // Off it, the call fails inside the continuation where nothing can
        // observe it, so the message is simply lost.
        //
        // That is the worst possible failure here and it hid for a while,
        // because it only bites when a result arrives within the throttle
        // window of its own start message. Every long Git operation was
        // slow enough to speak directly; a GitHub sign-in that fails in
        // 200 ms announced "Signing in to GitHub" and then nothing at all.
        try
        {
            if (MainThread.IsMainThread)
            {
                Speak(text);
                return;
            }

            MainThread.BeginInvokeOnMainThread(() => Speak(text));
        }
        catch (Exception)
        {
            // No MainThread outside a running app, which is the case in
            // tests. Speaking directly is right there.
            Speak(text);
        }
    }
}
