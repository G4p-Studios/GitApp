using GitApp.Domain;

namespace GitApp.Services;

/// <summary>
/// Windows toast notifications for newly arrived GitHub notifications.
///
/// A convenience over the inbox, never a replacement for it: toasts are
/// transient and easy to miss, so the inbox is the real surface and always has
/// the item (ARCHITECTURE 4.4). Implemented per platform, the same partial
/// pattern as <see cref="TokenStoreFactory"/>; a platform with no
/// implementation simply raises no toast, which is correct rather than broken.
///
/// Unverified on non-Windows and, until a Windows run, on Windows too: the
/// toast surface can only be confirmed by watching it appear and hearing a
/// screen reader read it. See docs/NOTIFICATIONS.md.
/// </summary>
public static partial class ToastModule
{
    /// <summary>Raised with a thread id when the user activates a toast.</summary>
    public static event Action<string>? Activated;

    /// <summary>Register the platform notification channel. Called once at startup.</summary>
    public static void Register() => RegisterPlatform();

    /// <summary>Retire the channel at shutdown.</summary>
    public static void Unregister() => UnregisterPlatform();

    /// <summary>Raise a toast for one notification. A no-op where unsupported.</summary>
    public static void Show(GitHubNotification notification) => ShowPlatform(notification);

    static partial void RegisterPlatform();

    static partial void UnregisterPlatform();

    static partial void ShowPlatform(GitHubNotification notification);

    /// <summary>Called by the platform layer when a toast is activated.</summary>
    internal static void RaiseActivated(string threadId) => Activated?.Invoke(threadId);
}
