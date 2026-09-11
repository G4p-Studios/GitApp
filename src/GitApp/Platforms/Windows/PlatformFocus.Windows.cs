using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace GitApp.Accessibility;

public static partial class PlatformFocus
{
    static partial void TryFocusFirstDescendantPlatform(VisualElement element, ref bool handled)
    {
        if (element.Handler?.PlatformView is not DependencyObject scope)
        {
            return;
        }

        // Deliberately not FocusManager.FindFirstFocusableElement. Inside a
        // CollectionView the first focusable thing it finds is the
        // ScrollViewer, which takes focus happily and reports to UI
        // Automation as an unnamed Pane. A screen reader then says nothing
        // useful, which is the exact failure the pane model exists to
        // prevent. We want an item, so look for one.
        var target = FindFirstItemContainer(scope) ?? FindFirstFocusableControl(scope);

        if (target is not null)
        {
            handled = target.Focus(FocusState.Programmatic);
        }
    }

    /// <summary>
    /// The row container. SelectorItem covers the ListView-based handler;
    /// ItemContainer covers the newer ItemsRepeater-based one. MAUI has
    /// switched between them, so both are checked rather than depending on
    /// whichever ships today.
    /// </summary>
    private static Control? FindFirstItemContainer(DependencyObject root)
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);

            if (child is SelectorItem or ItemContainer && child is Control container)
            {
                return container;
            }

            var found = FindFirstItemContainer(child);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>
    /// Fallback for panes whose entry is not a list. Skips ScrollViewer for
    /// the reason above.
    /// </summary>
    private static Control? FindFirstFocusableControl(DependencyObject root)
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);

            if (child is Control { IsTabStop: true, IsEnabled: true } control and not ScrollViewer)
            {
                return control;
            }

            var found = FindFirstFocusableControl(child);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    static partial void TrackFocusWithinPlatform(VisualElement element, string paneId)
    {
        void Hook()
        {
            if (element.Handler?.PlatformView is not UIElement view)
            {
                return;
            }

            view.GotFocus -= OnGotFocus;
            view.GotFocus += OnGotFocus;
        }

        void OnGotFocus(object sender, RoutedEventArgs e)
        {
            // Focus arrived here by some route other than F6, so tell the
            // manager where it is.
            FocusManager.Current.NoteActivePane(paneId);

            // Remember the container, not the data item: re-focusing the
            // container is what actually restores the caret position and the
            // screen reader's sense of place.
            if (e.OriginalSource is not Control container)
            {
                return;
            }

            FocusManager.Current.SetPaneRestore(
                paneId,
                () => container.IsLoaded && container.Focus(FocusState.Programmatic));
        }

        if (element.Handler is not null)
        {
            Hook();
            return;
        }

        element.HandlerChanged += OnHandlerChanged;

        void OnHandlerChanged(object? sender, EventArgs e)
        {
            element.HandlerChanged -= OnHandlerChanged;
            Hook();
        }
    }

    static partial void TryFocusSelectedItemPlatform(VisualElement element, ref bool handled)
    {
        if (element.Handler?.PlatformView is not DependencyObject scope)
        {
            return;
        }

        var selected = FindSelectedContainer(scope);
        if (selected is not null)
        {
            handled = selected.Focus(FocusState.Programmatic);
        }
    }

    /// <summary>
    /// The realized container for the selected row. Virtualization means it
    /// only exists once the row has been scrolled into view, so callers
    /// scroll first.
    /// </summary>
    private static Control? FindSelectedContainer(DependencyObject root)
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);

            if (child is SelectorItem { IsSelected: true } selector)
            {
                return selector;
            }

            if (child is ItemContainer { IsSelected: true } container)
            {
                return container;
            }

            var found = FindSelectedContainer(child);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }
}
