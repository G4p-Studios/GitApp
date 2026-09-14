using System.Collections.ObjectModel;
using GitApp.Accessibility;
using GitApp.Domain;
using GitApp.Services;

namespace GitApp.ViewModels;

/// <summary>
/// The inbox screen: unread notifications, newest first, each openable on
/// github.com or markable read. It binds through the shared
/// <see cref="NotificationService"/> so the list a background poll updates and
/// the list on screen are the same one. See docs/NOTIFICATIONS.md.
/// </summary>
public sealed class NotificationsViewModel : ObservableObject
{
    private readonly NotificationService _service;
    private readonly Announcer _announcer;

    private string _heading = "No unread notifications";
    private GitHubNotification? _selected;
    private bool _isBusy;

    public NotificationsViewModel(NotificationService service, Announcer? announcer = null)
    {
        _service = service;
        _announcer = announcer ?? Announcer.Current;

        RefreshCommand = new AsyncCommand(RefreshAsync, () => !IsBusy);
        MarkReadCommand = new AsyncCommand<GitHubNotification>(MarkReadAsync);
        MarkAllReadCommand = new AsyncCommand(MarkAllReadAsync, () => Items.Any(n => n.Unread));
        OpenCommand = new AsyncCommand<GitHubNotification>(OpenAsync);

        foreach (var notification in _service.Inbox.Items)
        {
            Items.Add(notification);
        }

        Heading = _service.Inbox.Heading;
        _service.InboxChanged += OnInboxChanged;
    }

    public ObservableCollection<GitHubNotification> Items { get; } = new();

    public AsyncCommand RefreshCommand { get; }

    public AsyncCommand<GitHubNotification> MarkReadCommand { get; }

    public AsyncCommand MarkAllReadCommand { get; }

    public AsyncCommand<GitHubNotification> OpenCommand { get; }

    public string Heading
    {
        get => _heading;
        private set => Set(ref _heading, value);
    }

    public GitHubNotification? Selected
    {
        get => _selected;
        set => Set(ref _selected, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (Set(ref _isBusy, value))
            {
                RefreshCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>
    /// Raised just before a selected row is removed, so the page can park
    /// focus on the heading. The Mark read button lives inside the doomed
    /// container; leaving it focused dumps onto Back (ARCHITECTURE 4.6).
    /// </summary>
    public event EventHandler? ParkFocus;

    /// <summary>
    /// Raised after a selected row was removed, so the page can put focus
    /// on the neighbour.
    /// </summary>
    public event EventHandler? FocusNeeded;

    /// <summary>The row with this thread id, so a toast activation can land on it.</summary>
    public GitHubNotification? Find(string id) => Items.FirstOrDefault(n => n.Id == id);

    private async Task RefreshAsync()
    {
        IsBusy = true;
        var done = _announcer.Operation("Checking for notifications");

        try
        {
            await _service.RefreshAsync();
            done(_service.Inbox.Heading);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task MarkReadAsync(GitHubNotification? notification)
    {
        if (notification is null)
        {
            return;
        }

        // Park on the heading *before* the network call, and yield so WinUI
        // actually moves focus. Focusing in the same stack as RemoveAt is
        // ignored: the Mark read button is still focused when its row dies,
        // and NVDA hears Back.
        Selected = notification;
        ParkFocus?.Invoke(this, EventArgs.Empty);
        if (Application.Current?.Dispatcher is { } dispatcher)
        {
            await dispatcher.DispatchAsync(() => { });
        }

        if (await _service.MarkReadAsync(notification))
        {
            _announcer.Announce($"Marked read. {notification.Title}");
        }
    }

    private async Task MarkAllReadAsync()
    {
        var unread = Items.Where(n => n.Unread).ToList();
        if (unread.Count == 0)
        {
            return;
        }

        foreach (var notification in unread)
        {
            await _service.MarkReadAsync(notification);
        }

        _announcer.Announce(unread.Count == 1 ? "Marked 1 read" : $"Marked {unread.Count} read");
    }

    private async Task OpenAsync(GitHubNotification? notification)
    {
        if (notification?.WebUrl is not { Length: > 0 } url)
        {
            return;
        }

        try
        {
            await Launcher.Default.OpenAsync(url);
            _announcer.Announce($"Opened {notification.Title} in your browser");
        }
        catch (Exception)
        {
            _announcer.Announce("Could not open your browser.");
        }
    }

    /// <summary>
    /// Reconcile the bound list in place, so a background poll leaves the
    /// rows it did not touch — and the cursor on them — alone (ARCHITECTURE
    /// 4.6). Removes, then updates, then inserts the new ones at the top,
    /// which is where GitHub's newest-first order puts them.
    /// </summary>
    private void OnInboxChanged(object? sender, InboxChange change)
    {
        void Apply()
        {
            Heading = _service.Inbox.Heading;

            foreach (var id in change.Removed)
            {
                var index = IndexOf(id);
                if (index < 0)
                {
                    continue;
                }

                RemoveAtKeepingFocus(index);
            }

            foreach (var updated in change.Updated)
            {
                var index = IndexOf(updated.Id);
                if (index < 0)
                {
                    continue;
                }

                // This screen is the unread inbox. A thread that is now read
                // leaves the list rather than sitting there looking identical
                // with an empty unread marker. Removing keeps the remaining
                // rows' containers intact; replacing the record would rebuild
                // the focused one and dump focus onto Back.
                if (!updated.Unread)
                {
                    RemoveAtKeepingFocus(index);
                    continue;
                }

                Items[index] = updated;
                if (Selected?.Id == updated.Id)
                {
                    Selected = updated;
                }
            }

            for (var i = 0; i < change.Added.Count; i++)
            {
                Items.Insert(i, change.Added[i]);
            }

            Heading = _service.Inbox.Heading;
            MarkAllReadCommand.RaiseCanExecuteChanged();
        }

        // Run inline when we are already on the UI thread so Mark read can
        // move focus off the doomed row *before* returning to announce.
        // Dispatching from here used to let the Back button speak first.
        if (Application.Current?.Dispatcher is { } dispatcher && dispatcher.IsDispatchRequired)
        {
            dispatcher.Dispatch(Apply);
        }
        else
        {
            Apply();
        }
    }

    /// <summary>
    /// Drop the row, then land on a neighbour. Focus was parked on the
    /// heading before the network call, so this remove does not destroy
    /// the focused control.
    /// </summary>
    private void RemoveAtKeepingFocus(int index)
    {
        var wasSelected = Selected?.Id == Items[index].Id;
        if (wasSelected)
        {
            Selected = Items.Count <= 1
                ? null
                : Items[index < Items.Count - 1 ? index + 1 : index - 1];
        }

        Items.RemoveAt(index);
        if (Items.Count == 0)
        {
            Selected = null;
        }

        if (wasSelected)
        {
            FocusNeeded?.Invoke(this, EventArgs.Empty);
        }
    }

    private int IndexOf(string id)
    {
        for (var i = 0; i < Items.Count; i++)
        {
            if (Items[i].Id == id)
            {
                return i;
            }
        }

        return -1;
    }
}
