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
        if (e.Key != VirtualKey.F6)
        {
            return;
        }

        var shift = InputKeyboardSource
            .GetKeyStateForCurrentThread(VirtualKey.Shift)
            .HasFlag(CoreVirtualKeyStates.Down);

        if (shift)
        {
            Previous();
        }
        else
        {
            Next();
        }

        // Mark handled so the key does not also reach the focused control.
        e.Handled = true;
    }
}
