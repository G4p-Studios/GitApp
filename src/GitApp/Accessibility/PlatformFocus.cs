namespace GitApp.Accessibility;

/// <summary>
/// Focusing a composite control.
///
/// Calling Focus() on a CollectionView does not move focus onto a row: the
/// list itself is not the focusable thing, its item containers are. Left
/// alone, entering the sidebar with F6 lands on the pane wrapper and the user
/// hears nothing useful, which is exactly the failure the pane model exists
/// to prevent.
///
/// Windows has a built-in answer in Microsoft.UI.Xaml.Input.FocusManager, so
/// the fallback is a platform hook rather than a hand-rolled visual tree
/// walk. Platforms without an implementation simply keep the plain Focus()
/// result.
/// </summary>
public static partial class PlatformFocus
{
    /// <summary>
    /// Move focus to the first focusable thing inside <paramref name="element"/>.
    /// Returns false when the platform has no implementation or nothing
    /// inside can take focus.
    /// </summary>
    public static bool TryFocusFirstDescendant(VisualElement element)
    {
        var handled = false;
        TryFocusFirstDescendantPlatform(element, ref handled);
        return handled;
    }

    static partial void TryFocusFirstDescendantPlatform(VisualElement element, ref bool handled);

    /// <summary>
    /// Watch focus moving inside a composite control and tell the
    /// FocusManager how to put it back.
    ///
    /// A list row is a platform item container, not a MAUI VisualElement, so
    /// the pane cannot simply hold a reference to it. Without this, F6 back
    /// into a list always lands on the first row and the user loses their
    /// place.
    /// </summary>
    public static void TrackFocusWithin(VisualElement element, string paneId) =>
        TrackFocusWithinPlatform(element, paneId);

    static partial void TrackFocusWithinPlatform(VisualElement element, string paneId);
}
