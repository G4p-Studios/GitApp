using GitApp.Domain;

namespace GitApp.Services;

/// <summary>
/// What one poll changed about the inbox, so a bound list can be mutated in
/// place rather than rebuilt.
///
/// ARCHITECTURE 4.6 is the reason this is a diff and not just the new list:
/// replacing an <c>ObservableCollection</c> wholesale rebuilds every row, which
/// throws away the focus position and the screen reader's sense of place. The
/// view applies <see cref="Removed"/>, <see cref="Added"/> and
/// <see cref="Updated"/> to the rows that actually changed and leaves the rest
/// untouched, so a background poll never moves the cursor.
/// </summary>
public sealed record InboxChange(
    IReadOnlyList<GitHubNotification> Added,
    IReadOnlyList<GitHubNotification> Updated,
    IReadOnlyList<string> Removed)
{
    public static readonly InboxChange None =
        new(Array.Empty<GitHubNotification>(), Array.Empty<GitHubNotification>(), Array.Empty<string>());

    public bool AnyChange => Added.Count > 0 || Updated.Count > 0 || Removed.Count > 0;
}

/// <summary>
/// The inbox, held apart from any window so the merge rules are testable.
///
/// GitHub returns the whole unread set on every poll, newest first. Handing
/// that straight to the view each time would rebuild the list on a timer; this
/// holds the authoritative set instead and reports only what moved, so the view
/// can keep the rows that did not (ARCHITECTURE 4.6). It also owns the read
/// state, because marking a thread read is a local change the next poll should
/// not undo or re-announce.
/// </summary>
public sealed class NotificationInbox
{
    private readonly List<GitHubNotification> _items = new();

    /// <summary>The inbox in display order, newest first.</summary>
    public IReadOnlyList<GitHubNotification> Items => _items;

    public int UnreadCount => _items.Count(n => n.Unread);

    /// <summary>The heading over the list, in words with a count. See ARCHITECTURE 3.3.</summary>
    public string Heading => UnreadCount switch
    {
        0 => "No unread notifications",
        1 => "1 unread notification",
        _ => $"{UnreadCount} unread notifications",
    };

    /// <summary>
    /// Merge a poll result and report what changed. A 304 (nothing changed)
    /// leaves the inbox and returns <see cref="InboxChange.None"/>.
    /// </summary>
    public InboxChange Apply(NotificationPage page)
    {
        if (page.NotModified)
        {
            return InboxChange.None;
        }

        var previous = _items.ToDictionary(n => n.Id, StringComparer.Ordinal);
        var incomingIds = new HashSet<string>(page.Items.Select(n => n.Id), StringComparer.Ordinal);

        var added = new List<GitHubNotification>();
        var updated = new List<GitHubNotification>();

        foreach (var notification in page.Items)
        {
            if (!previous.TryGetValue(notification.Id, out var existing))
            {
                added.Add(notification);
            }
            else if (existing.UpdatedAt != notification.UpdatedAt || existing.Unread != notification.Unread)
            {
                updated.Add(notification);
            }
        }

        var removed = previous.Keys.Where(id => !incomingIds.Contains(id)).ToList();

        // The page's order is GitHub's own, newest first, and becomes ours.
        _items.Clear();
        _items.AddRange(page.Items);

        return new InboxChange(added, updated, removed);
    }

    /// <summary>
    /// Mark one thread read locally, in place, and report whether anything
    /// changed. The record is replaced rather than mutated because it is
    /// immutable; the row keeps its position so focus does not move.
    /// </summary>
    public bool MarkRead(string id)
    {
        var index = _items.FindIndex(n => n.Id == id);
        if (index < 0 || !_items[index].Unread)
        {
            return false;
        }

        _items[index] = _items[index] with { Unread = false };
        return true;
    }

    /// <summary>Forget everything, for sign-out. A signed-out inbox is empty, not stale.</summary>
    public void Clear() => _items.Clear();
}
