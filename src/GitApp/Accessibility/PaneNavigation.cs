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
    /// Call once from the page that owns the panes, after its content is set.
    /// </summary>
    public static void Attach(Page page)
    {
        HookAnnouncer();
        AttachPlatform(page);
    }

    /// <summary>
    /// Moves to the next or previous difference in the diff viewer. Set by
    /// the page, which owns the list. Returns false when there is nothing to
    /// move to, so the key can fall through rather than appearing to do
    /// nothing.
    /// </summary>
    public static Func<int, bool>? HunkNavigator { get; set; }

    internal static bool MoveToHunk(int direction) => HunkNavigator?.Invoke(direction) ?? false;

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
    public static void FocusFirstPane()
    {
        var first = FocusManager.Current.FirstPane;
        if (first is not null)
        {
            FocusManager.Current.FocusPane(first.Id, announce: false);
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
