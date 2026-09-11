using GitApp.Accessibility;
using GitApp.Domain;
using GitApp.GitHub;

namespace GitApp.Services;

/// <summary>
/// The background poll loop that keeps the inbox current and raises a toast for
/// anything new.
///
/// App-wide and self-guarding: it starts once and simply idles while signed
/// out, waking on a short timer only to ask the scheduler whether a poll is
/// due. That way it needs no sign-in event to come to life, and the polite
/// polling rules — conditional requests, the honoured interval, backoff — all
/// live in <see cref="NotificationPoller"/> and <see cref="NotificationInbox"/>
/// where they are unit tested. See docs/NOTIFICATIONS.md.
///
/// The loop itself, being a timer and a session, is the part that a Windows run
/// still has to confirm end to end.
/// </summary>
public sealed class NotificationService
{
    /// <summary>How often the loop wakes to check whether a poll is due.</summary>
    private static readonly TimeSpan Tick = TimeSpan.FromSeconds(15);

    private readonly GitHubSession _session;
    private readonly Announcer _announcer;
    private readonly NotificationPoller _poller = new();

    private IDispatcherTimer? _timer;
    private bool _polling;

    public NotificationService(GitHubSession session, Announcer? announcer = null)
    {
        _session = session;
        _announcer = announcer ?? Announcer.Current;
        ToastModule.Activated += id => OpenRequested?.Invoke(this, id);
    }

    /// <summary>The authoritative inbox. The screen binds through this.</summary>
    public NotificationInbox Inbox { get; } = new();

    /// <summary>Raised after a poll changes the inbox, so a bound list can reconcile in place.</summary>
    public event EventHandler<InboxChange>? InboxChanged;

    /// <summary>Raised when a toast is activated, carrying the thread id to open.</summary>
    public event EventHandler<string>? OpenRequested;

    public void Start()
    {
        if (_timer is not null || Application.Current?.Dispatcher is not { } dispatcher)
        {
            return;
        }

        _timer = dispatcher.CreateTimer();
        _timer.Interval = Tick;
        _timer.Tick += OnTick;
        _timer.Start();
    }

    public void Stop()
    {
        if (_timer is not null)
        {
            _timer.Tick -= OnTick;
            _timer.Stop();
            _timer = null;
        }
    }

    /// <summary>Poll now, whatever the schedule says. For the Refresh button.</summary>
    public Task RefreshAsync() => PollAsync();

    /// <summary>Forget everything on sign-out. A signed-out inbox is empty, not stale.</summary>
    public void SignedOut() => Inbox.Clear();

    private async void OnTick(object? sender, EventArgs e)
    {
        if (_session.IsSignedIn && _poller.IsDue(DateTimeOffset.UtcNow))
        {
            await PollAsync();
        }
    }

    private async Task PollAsync()
    {
        // Never overlap two polls: a slow request must not stack behind the
        // timer and fire a burst the moment it returns.
        if (_polling || _session.CreateClient() is not { } client)
        {
            return;
        }

        _polling = true;

        try
        {
            var result = await client.GetNotificationsAsync(_poller.PollToken);
            var now = DateTimeOffset.UtcNow;

            if (!result.Success)
            {
                _poller.Backoff(now, result.Failure);
                return;
            }

            var page = result.Value!;
            var fresh = _poller.Observe(page, now);
            var change = Inbox.Apply(page);

            if (change.AnyChange)
            {
                InboxChanged?.Invoke(this, change);
            }

            // The toast is the announcement: a screen reader speaks it as it
            // appears, so announcing on top would say it twice (ARCHITECTURE
            // 3.5). Nothing is spoken here.
            foreach (var notification in fresh)
            {
                ToastModule.Show(notification);
            }
        }
        finally
        {
            client.Dispose();
            _polling = false;
        }
    }

    /// <summary>
    /// Mark one thread read on GitHub and locally. The local change is in
    /// place, so the row keeps its position and focus does not move.
    /// </summary>
    public async Task<bool> MarkReadAsync(GitHubNotification notification)
    {
        if (_session.CreateClient() is not { } client)
        {
            return false;
        }

        try
        {
            var result = await client.MarkThreadReadAsync(notification.Id);

            if (result.Success && Inbox.MarkRead(notification.Id))
            {
                InboxChanged?.Invoke(this, new InboxChange(
                    Array.Empty<GitHubNotification>(),
                    new[] { notification with { Unread = false } },
                    Array.Empty<string>()));
            }

            if (!result.Success)
            {
                _announcer.Announce(result.Error!, Urgency.Assertive);
            }

            return result.Success;
        }
        finally
        {
            client.Dispose();
        }
    }
}
