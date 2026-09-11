using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using Windows.UI.Core;

namespace GitApp.Accessibility;

/// <summary>
/// The Windows half of pane navigation.
///
/// MAUI has no cross-platform way to observe a keystroke that no control has
/// claimed, so F6 has to be hooked on the native element. PreviewKeyDown
/// tunnels, so it runs before the CollectionView gets a chance to treat the
/// key as its own.
/// </summary>
public static partial class PaneNavigation
{
    static partial void AttachPlatform(Page page)
    {
        // The handler may not exist yet when a page is constructed, so wait
        // for it rather than silently doing nothing.
        if (page.Handler?.PlatformView is FrameworkElement ready)
        {
            Hook(ready);
            return;
        }

        page.HandlerChanged += OnHandlerChanged;

        void OnHandlerChanged(object? sender, EventArgs e)
        {
            if (page.Handler?.PlatformView is FrameworkElement element)
            {
                page.HandlerChanged -= OnHandlerChanged;
                Hook(element);
            }
        }
    }

    private static void Hook(FrameworkElement element)
    {
        element.PreviewKeyDown -= OnPreviewKeyDown;
        element.PreviewKeyDown += OnPreviewKeyDown;
    }

    private static void OnPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key is not (VirtualKey.F6 or VirtualKey.F7 or VirtualKey.Enter
            or VirtualKey.Space or VirtualKey.Right or VirtualKey.Left
            or VirtualKey.Escape))
        {
            return;
        }

        // Escape backs out one level (ARCHITECTURE 3.4). Only where the
        // screen says it means something: swallowing it everywhere would
        // break the escape that closes a picker or cancels an edit.
        if (e.Key == VirtualKey.Escape)
        {
            if (Back())
            {
                e.Handled = true;
            }

            return;
        }

        var shift = InputKeyboardSource
            .GetKeyStateForCurrentThread(VirtualKey.Shift)
            .HasFlag(CoreVirtualKeyStates.Down);

        // Folding a difference. Enter and Space toggle, matching the
        // expanders in Settings; Right and Left say which way they mean,
        // matching a tree view. The handler declines whenever the cursor is
        // not on a foldable header, so these keys behave normally
        // everywhere else in the window.
        //
        // Enter and Space also open a folder on the repository screen.
        // Tried after Expand so the diff viewer keeps the key when it
        // applies, and skipped when Expand already handled it.
        if (e.Key is VirtualKey.Enter or VirtualKey.Space)
        {
            if (Expand(null) || Activate())
            {
                e.Handled = true;
            }

            return;
        }

        if (e.Key is VirtualKey.Right or VirtualKey.Left)
        {
            var intent = e.Key == VirtualKey.Right;

            if (Expand(intent))
            {
                e.Handled = true;
            }

            return;
        }

        if (e.Key == VirtualKey.F6)
        {
            if (shift)
            {
                Previous();
            }
            else
            {
                Next();
            }
        }
        else
        {
            // F7 and Shift+F7 move between differences, matching VS Code's
            // accessible diff viewer so the muscle memory transfers.
            if (!MoveToHunk(shift ? -1 : 1))
            {
                return;
            }
        }

        // Mark handled so the key does not also reach the focused control.
        e.Handled = true;
    }
}
