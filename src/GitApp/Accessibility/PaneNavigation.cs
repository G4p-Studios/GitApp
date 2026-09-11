namespace GitApp.Accessibility;

/// <summary>
/// Wires F6 and Shift+F6 to pane cycling, and announces the destination.
///
/// The key hook itself is platform code, because MAUI has no cross-platform
/// way to observe a keystroke that no control has claimed. See
/// Platforms/Windows/PaneNavigation.Windows.cs. Everything above that seam
/// lives here so the behaviour is defined once.
/// </summary>
public static partial class PaneNavigation
{
    private static bool _announcerHooked;

    /// <summary>
    /// Hand the whole pane model over to one screen.
    ///
    /// Called every time a page appears, not once at construction. With
    /// more than one screen the registry has to be emptied and refilled,
    /// or F6 cycles to regions belonging to a page that is no longer
    /// visible: focus goes nowhere and nothing is announced. The keys go
    /// the same way, so a handler belonging to another screen cannot
    /// swallow a keystroke meant for this one.
    /// </summary>
    public static void Attach(Page page)
    {
        HookAnnouncer();

        HunkNavigator = null;
        RowExpander = null;
        BackHandler = null;

        FocusManager.Current.ResetPanes();

        foreach (var pane in Pane.Within(page))
        {
            pane.Register();
        }

        AttachPlatform(page);
    }

    /// <summary>
    /// What Escape does on this screen. Null where Escape means nothing,
    /// so the key falls through rather than appearing to be swallowed.
    /// </summary>
    public static Func<bool>? BackHandler { get; set; }

    internal static bool Back() => BackHandler?.Invoke() ?? false;

    /// <summary>
    /// Moves to the next or previous difference in the diff viewer. Set by
    /// the page, which owns the list. Returns false when there is nothing to
    /// move to, so the key can fall through rather than appearing to do
    /// nothing.
    /// </summary>
    public static Func<int, bool>? HunkNavigator { get; set; }

    internal static bool MoveToHunk(int direction) => HunkNavigator?.Invoke(direction) ?? false;

    /// <summary>
    /// Folds or unfolds the difference under the cursor in the diff viewer.
    /// Null toggles; true and false are the arrow keys, which say which
    /// direction they mean rather than flipping whatever is there.
    ///
    /// Returns false when the key does not apply here, so Enter and Space
    /// keep working everywhere else in the window.
    /// </summary>
    public static Func<bool?, bool>? RowExpander { get; set; }

    internal static bool Expand(bool? open) => RowExpander?.Invoke(open) ?? false;

    /// <summary>Next pane. Bound to F6.</summary>
    public static void Next() => FocusManager.Current.CyclePane(1);

    /// <summary>Previous pane. Bound to Shift+F6.</summary>
    public static void Previous() => FocusManager.Current.CyclePane(-1);

    /// <summary>
    /// Put focus somewhere meaningful once the window is up.
    ///
    /// Without this the window has no focused control, which shows up as no
    /// focus indicator and, on some stacks, a screen reader with nothing to
    /// announce beyond the title. Silent, because the screen reader already
    /// announces the window and the newly focused control on activation, and
    /// our own "X pane" on top of that is duplicate speech.
    /// </summary>
    /// <summary>
    /// <paramref name="announce"/> is false at startup, where the screen
    /// reader already announces the window and the focused control, and
    /// true when arriving on a screen mid-session, where nothing else says
    /// the screen changed.
    /// </summary>
    public static bool FocusFirstPane(bool announce = false)
    {
        var first = FocusManager.Current.FirstPane;

        return first is not null && FocusManager.Current.TryFocusPane(first.Id, announce);
    }

    /// <summary>
    /// Put focus on the first pane, now or as soon as the screen can take
    /// it.
    ///
    /// A page that has just been swapped in has no loaded controls yet, so
    /// the first attempt fails silently and focus stays on whatever the
    /// previous screen had. That is the state where a screen reader user
    /// has nothing read, nothing highlighted, and no way to tell the screen
    /// changed at all. So it is attempted again on load, and once more on a
    /// short delay for the case where the page was already loaded from a
    /// previous visit and Loaded never fires again.
    /// </summary>
    public static void FocusFirstPaneWhenReady(Page page, bool announce = false)
    {
        if (FocusFirstPane(announce))
        {
            return;
        }

        page.Loaded -= OnLoaded;
        page.Loaded += OnLoaded;

        page.Dispatcher.DispatchDelayed(
            TimeSpan.FromMilliseconds(200), () => FocusFirstPane(announce));

        void OnLoaded(object? sender, EventArgs e)
        {
            page.Loaded -= OnLoaded;
            FocusFirstPane(announce);
        }
    }

    private static void HookAnnouncer()
    {
        if (_announcerHooked)
        {
            return;
        }

        _announcerHooked = true;

        // Announce the destination, not the departure. Someone moving quickly
        // needs to know where they landed.
        FocusManager.Current.PaneEntered += (_, pane) =>
            Announcer.Current.Announce($"{pane.Name} pane");
    }

    /// <summary>
    /// Implemented per platform. Absent platforms simply get no F6, which is
    /// correct: it is a Windows convention, and macOS will need its own
    /// region-navigation gesture rather than this one.
    /// </summary>
    static partial void AttachPlatform(Page page);
}
