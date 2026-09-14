namespace GitApp.Accessibility;

/// <summary>
/// Focus ownership: the pane registry behind F6 cycling, and a restore stack
/// for transient surfaces.
///
/// Much less is needed here than in the React Native build. MAUI's
/// CollectionView already supplies roving focus, arrow keys, Home and End,
/// set positions and initial focus, all of which were hand-written before
/// (see docs/SPIKE-MAUI.md). What the framework does not supply is a way to
/// move between regions of a window, and a place to remember where focus
/// should return to when a menu or dialog closes.
///
/// See docs/ARCHITECTURE.md section 3.4.
/// </summary>
public sealed class FocusManager
{
    public static FocusManager Current { get; } = new();

    private readonly List<PaneRegistration> _panes = new();
    private readonly Stack<VisualElement?> _restore = new();
    private string? _activePaneId;

    /// <summary>Raised when focus enters a pane, so the shell can announce it.</summary>
    public event EventHandler<PaneRegistration>? PaneEntered;

    // -----------------------------------------------------------------
    // Panes
    // -----------------------------------------------------------------

    public IDisposable RegisterPane(string id, string name, int order, VisualElement container)
    {
        var existing = Find(id);
        if (existing is not null)
        {
            // Re-registration must not discard a claim the content already
            // made; a pane re-registers whenever its name or order changes.
            existing.Name = name;
            existing.Order = order;
            existing.Container = container;
            _panes.Sort((a, b) => a.Order.CompareTo(b.Order));
            return new Unregister(this, id);
        }

        _panes.Add(new PaneRegistration(id, name, order, container));
        _panes.Sort((a, b) => a.Order.CompareTo(b.Order));
        _activePaneId ??= id;

        return new Unregister(this, id);
    }

    /// <summary>
    /// A content control claims its pane's entry point, so entering the pane
    /// lands on the list rather than on the wrapper. Without this the user
    /// hears the pane name and then has to Tab again to reach anything.
    /// </summary>
    public void SetPaneEntry(string id, VisualElement? entry)
    {
        var pane = Find(id);
        if (pane is not null)
        {
            pane.Entry = entry;
        }
    }

    /// <summary>
    /// Remember where focus sits inside a pane, so returning by F6 lands
    /// where the user left off rather than at the top. This is what makes
    /// pane cycling feel like a Windows app instead of a reset button.
    /// </summary>
    public void NoteFocusWithin(string id, VisualElement? target)
    {
        var pane = Find(id);
        if (pane is not null)
        {
            pane.LastFocused = target;
        }

        _activePaneId = id;
    }

    /// <summary>
    /// Record that focus is now inside a pane, without disturbing where it
    /// was last time.
    ///
    /// Cycling is not the only way into a pane: a mouse click, a Tab, or
    /// F7 jumping into the diff all move focus without going through
    /// <see cref="CyclePane"/>. Without this the manager keeps believing
    /// focus is wherever it last put it, so the next F6 cycles from the
    /// wrong place and any key bound to "the pane you are in" is offered to
    /// the wrong pane.
    /// </summary>
    public void NoteActivePane(string id)
    {
        if (Find(id) is not null)
        {
            _activePaneId = id;
        }
    }

    /// <summary>
    /// Record how to restore focus inside a pane. Called by the platform
    /// focus tracker as the user moves through a list.
    /// </summary>
    public void SetPaneRestore(string id, Func<bool>? restore)
    {
        var pane = Find(id);
        if (pane is not null)
        {
            pane.RestoreLastFocus = restore;
        }
    }

    /// <summary>
    /// Forget every pane, because a different screen is taking over.
    ///
    /// Registration used to rely on Pane.Loaded and Pane.Unloaded, which is
    /// fine while there is one screen and wrong the moment there are two:
    /// swapping the window's page does not reliably unload the old one, so
    /// its panes stayed in the registry and F6 cycled to regions that were
    /// no longer on screen. Focus went nowhere and nothing was announced.
    /// The screen being attached now says what its panes are.
    /// </summary>
    public void ResetPanes()
    {
        _panes.Clear();
        _activePaneId = null;
    }

    public PaneRegistration? FirstPane => _panes.FirstOrDefault(Visible);

    private static bool Visible(PaneRegistration pane) => pane.Container.IsVisible;

    public PaneRegistration? ActivePane => _activePaneId is null ? null : Find(_activePaneId);

    public IReadOnlyList<PaneRegistration> Panes => _panes;

    /// <summary>
    /// Move to the next or previous pane and focus it. Returns the pane
    /// entered, or null when there is nowhere to go.
    /// </summary>
    public PaneRegistration? CyclePane(int direction)
    {
        // One pane still has to be reachable. The notifications inbox has
        // Back / Refresh sitting outside it, and F6 is how you return from
        // those. Doing nothing when Count < 2 left the user stranded on Back.
        var reachable = _panes.Where(Visible).ToList();
        if (reachable.Count == 0)
        {
            return null;
        }

        if (reachable.Count == 1)
        {
            Enter(reachable[0], announce: true);
            return reachable[0];
        }

        var currentIndex = reachable.FindIndex(p => p.Id == _activePaneId);
        var from = currentIndex < 0 ? 0 : currentIndex;
        var index = ((from + direction) % reachable.Count + reachable.Count) % reachable.Count;
        var next = reachable[index];

        Enter(next, announce: true);
        return next;
    }

    /// <summary>
    /// Jump directly to a pane.
    ///
    /// announce false moves focus without notifying subscribers. Used for the
    /// initial focus at startup, where the screen reader already announces the
    /// window and the newly focused control, and our own "X pane" on top of
    /// that is duplicate speech.
    /// </summary>
    public PaneRegistration? FocusPane(string id, bool announce = true)
    {
        var pane = Find(id);
        if (pane is null)
        {
            return null;
        }

        Enter(pane, announce);
        return pane;
    }

    /// <summary>
    /// Focus a pane and say whether focus actually landed there.
    ///
    /// The distinction matters on a screen that has only just appeared:
    /// its controls are not loaded yet, focus quietly fails, and announcing
    /// the pane anyway tells the user they are somewhere they are not.
    /// </summary>
    public bool TryFocusPane(string id, bool announce)
    {
        var pane = Find(id);
        return pane is not null && Enter(pane, announce);
    }

    // -----------------------------------------------------------------
    // Restore stack
    // -----------------------------------------------------------------

    /// <summary>
    /// Record where focus should return to, before opening something that
    /// takes it. Call this while the outgoing element still exists.
    /// </summary>
    public void PushRestoreTarget(VisualElement? target) => _restore.Push(target);

    /// <summary>
    /// Return focus to the most recently pushed target. Safe when that target
    /// has since disappeared: focus falls back to the active pane rather than
    /// to the window root, which is the state that strands a screen reader
    /// user.
    /// </summary>
    public void PopAndRestore()
    {
        var target = _restore.Count > 0 ? _restore.Pop() : null;

        if (TryFocus(target))
        {
            return;
        }

        FocusActivePane();
    }

    public void DropRestoreTarget()
    {
        if (_restore.Count > 0)
        {
            _restore.Pop();
        }
    }

    public int RestoreDepth => _restore.Count;

    /// <summary>Test seam.</summary>
    public void Reset()
    {
        _panes.Clear();
        _restore.Clear();
        _activePaneId = null;
    }

    // -----------------------------------------------------------------

    private PaneRegistration? Find(string id) => _panes.FirstOrDefault(p => p.Id == id);

    private void FocusActivePane()
    {
        var pane = ActivePane;
        if (pane is null)
        {
            return;
        }

        if (!TryRestore(pane) && !TryFocus(pane.LastFocused) && !TryFocus(pane.Entry))
        {
            TryFocus(pane.Container);
        }
    }

    private static bool TryRestore(PaneRegistration pane)
    {
        if (pane.RestoreLastFocus is null)
        {
            return false;
        }

        try
        {
            return pane.RestoreLastFocus();
        }
        catch
        {
            // The remembered container was recycled out from under us, which
            // virtualized lists do routinely. Fall through to the entry.
            return false;
        }
    }

    private bool Enter(PaneRegistration pane, bool announce)
    {
        // Where the user last was, then the content's entry point, then the
        // container as a last resort.
        var focused = TryRestore(pane)
            || TryFocus(pane.LastFocused)
            || TryFocus(pane.Entry)
            || TryFocus(pane.Container);

        if (!focused)
        {
            // Saying nothing is right here. The alternative is announcing a
            // pane the user has not been moved into, which is worse than
            // silence because it cannot be corrected by listening.
            return false;
        }

        _activePaneId = pane.Id;

        if (announce)
        {
            PaneEntered?.Invoke(this, pane);
        }

        return true;
    }

    private static bool TryFocus(VisualElement? element)
    {
        if (element is null || !element.IsLoaded || !element.IsEnabled)
        {
            return false;
        }

        try
        {
            // Composite controls need the platform first. Calling Focus() on
            // a CollectionView succeeds, but lands on its scroll host rather
            // than a row: UI Automation reports an unnamed Pane and a screen
            // reader has nothing to say. Ask the platform for the first
            // focusable descendant instead, which is an actual item.
            if (element is ItemsView && PlatformFocus.TryFocusFirstDescendant(element))
            {
                return true;
            }

            if (element.Focus())
            {
                return true;
            }

            // Anything else that reports false may still have something
            // focusable inside it.
            return PlatformFocus.TryFocusFirstDescendant(element);
        }
        catch
        {
            // The element went away between capture and restore. Not
            // exceptional; the caller falls through to the next candidate.
            return false;
        }
    }

    private sealed class Unregister : IDisposable
    {
        private readonly FocusManager _owner;
        private readonly string _id;

        public Unregister(FocusManager owner, string id)
        {
            _owner = owner;
            _id = id;
        }

        public void Dispose()
        {
            var pane = _owner.Find(_id);
            if (pane is not null)
            {
                _owner._panes.Remove(pane);
            }

            if (_owner._activePaneId == _id)
            {
                _owner._activePaneId = _owner._panes.FirstOrDefault()?.Id;
            }
        }
    }
}

public sealed class PaneRegistration
{
    public PaneRegistration(string id, string name, int order, VisualElement container)
    {
        Id = id;
        Name = name;
        Order = order;
        Container = container;
    }

    public string Id { get; }

    /// <summary>Spoken on entry. A noun, not a sentence: "Repositories".</summary>
    public string Name { get; set; }

    /// <summary>Cycle position. Leave gaps so panes can be inserted later.</summary>
    public int Order { get; set; }

    /// <summary>Last-resort focus target when the pane holds nothing focusable.</summary>
    public VisualElement Container { get; set; }

    /// <summary>A content control that claimed the entry point.</summary>
    public VisualElement? Entry { get; set; }

    /// <summary>Where focus was when the user last left this pane.</summary>
    public VisualElement? LastFocused { get; set; }

    /// <summary>
    /// How to put focus back exactly where it was, when the thing that had it
    /// is not a MAUI element we can hold.
    ///
    /// A CollectionView row is a platform item container, not a VisualElement,
    /// so LastFocused cannot represent it. Without this, F6 back into a list
    /// always lands on the first row, and in a list of a thousand commits the
    /// user loses their place every time they check something in another
    /// pane. See docs/ARCHITECTURE.md section 3.4.
    /// </summary>
    public Func<bool>? RestoreLastFocus { get; set; }
}
