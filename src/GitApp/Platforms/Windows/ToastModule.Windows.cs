using GitApp.Domain;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace GitApp.Services;

/// <summary>
/// The Windows half of the toast module, over Windows App SDK app
/// notifications.
///
/// The toast body repeats the row's own content order — reason, then what,
/// then where — so hearing the toast and later hearing the row in the inbox
/// say the same thing in the same order, rather than two descriptions of one
/// event that do not match.
///
/// App notifications need package identity; ARCHITECTURE 4.4 records that
/// GitApp ships a sparse package for exactly this. Registering an unpackaged
/// build still shows toasts for the running session, which is enough to
/// verify the surface; persistent activation after exit is what the sparse
/// package adds.
/// </summary>
public static partial class ToastModule
{
    private const string ThreadArgument = "threadId";

    private static bool _registered;

    static partial void RegisterPlatform()
    {
        if (_registered)
        {
            return;
        }

        var manager = AppNotificationManager.Default;
        manager.NotificationInvoked += OnInvoked;
        manager.Register();
        _registered = true;
    }

    static partial void UnregisterPlatform()
    {
        if (!_registered)
        {
            return;
        }

        var manager = AppNotificationManager.Default;
        manager.NotificationInvoked -= OnInvoked;
        manager.Unregister();
        _registered = false;
    }

    static partial void ShowPlatform(GitHubNotification notification)
    {
        var builder = new AppNotificationBuilder()
            .AddArgument(ThreadArgument, notification.Id)
            .AddText(notification.Title)
            .AddText($"{notification.ReasonWord}, {notification.SubjectWord}, in {notification.RepositoryFullName}");

        AppNotificationManager.Default.Show(builder.BuildNotification());
    }

    private static void OnInvoked(AppNotificationManager sender, AppNotificationActivatedEventArgs args)
    {
        if (args.Arguments.TryGetValue(ThreadArgument, out var id) && !string.IsNullOrEmpty(id))
        {
            RaiseActivated(id);
        }
    }
}
