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

    /// <summary>
    /// Move focus to the currently selected item inside a list.
    ///
    /// Selecting a row programmatically does not focus it, so without this a
    /// jump like F7 would announce the destination and leave the user's
    /// focus behind, unable to read on from where they landed.
    /// </summary>
    public static bool TryFocusSelectedItem(VisualElement element)
    {
        var handled = false;
        TryFocusSelectedItemPlatform(element, ref handled);
        return handled;
    }

    static partial void TryFocusSelectedItemPlatform(VisualElement element, ref bool handled);

    /// <summary>
    /// Focus a specific element, including labels that are not tab stops.
    /// Used to park focus outside a CollectionView before a row is removed,
    /// so destroying the focused Mark read button does not dump onto Back.
    /// </summary>
    public static bool TryFocusElement(VisualElement element)
    {
        var handled = false;
        TryFocusElementPlatform(element, ref handled);
        if (handled)
        {
            return true;
        }

        return element.Focus();
    }

    static partial void TryFocusElementPlatform(VisualElement element, ref bool handled);

    /// <summary>
    /// Give an empty list's placeholder something to say.
    ///
    /// A CollectionView with no items still holds one tab stop: the control
    /// that hosts the EmptyView. It has no automation peer, so tabbing onto
    /// it moves focus to a thing UI Automation cannot describe, and a screen
    /// reader says nothing at all. A silent stop is worse than either
    /// alternative, because it is indistinguishable from the app having
    /// stopped responding.
    ///
    /// Skipping it would be the easy fix and the wrong one: "there is
    /// nothing here" is real information, and it is the one thing a sighted
    /// user gets for free from the placeholder text on screen. So the stop
    /// stays, and it says the same thing the placeholder does.
    /// </summary>
    public static void DescribeEmptyView(VisualElement element, string message) =>
        DescribeEmptyViewPlatform(element, message);

    static partial void DescribeEmptyViewPlatform(VisualElement element, string message);

    /// <summary>
    /// Override the spoken control type, so a Button used as a heading is
    /// heard as a heading rather than as a button.
    /// </summary>
    public static void SetLocalizedRole(VisualElement element, string role) =>
        SetLocalizedRolePlatform(element, role);

    static partial void SetLocalizedRolePlatform(VisualElement element, string role);
}
