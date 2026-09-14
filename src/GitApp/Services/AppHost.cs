using GitApp.Accessibility;
using GitApp.Domain;
using GitApp.GitHub;
using GitApp.ViewModels;

namespace GitApp.Services;

/// <summary>
/// The one MainViewModel, the one GitHub session, and the screens that
/// jump between Home, Local, and GitHub. The window's root is Home; the
/// others are pushed on top so Escape still means back (docs/HOME.md).
/// </summary>
public static class AppHost
{
    public static MainViewModel Main { get; } = new();

    public static DashboardPage? Home { get; set; }

    public static MainPage? Local { get; set; }

    public static GitHubPage? GitHub { get; set; }

    public static GitHubSession Session => Main.GitHub;

    public static NotificationService Notifications => Main.Notifications;

    public static void ShowHome()
    {
        while (AppNavigator.CanGoBack)
        {
            AppNavigator.GoBack();
        }

        Announcer.Current.Announce("Home");
    }

    public static void ShowLocal()
    {
        Local ??= new MainPage();
        if (Application.Current?.Windows.FirstOrDefault()?.Page == Local)
        {
            return;
        }

        AppNavigator.Show(Local);
        Announcer.Current.Announce("Local repositories");
    }

    public static void ShowGitHub()
    {
        EnsureGitHub();
        if (Application.Current?.Windows.FirstOrDefault()?.Page == GitHub)
        {
            return;
        }

        AppNavigator.Show(GitHub!);
    }

    public static void ShowNotifications(string? threadId = null)
    {
        EnsureGitHub();
        if (Application.Current?.Windows.FirstOrDefault()?.Page is NotificationsPage inbox)
        {
            if (!string.IsNullOrEmpty(threadId))
            {
                inbox.FocusThread(threadId);
            }

            return;
        }

        GitHub!.ShowNotifications(threadId);
    }

    public static void ShowRepository(GitHubRepository repo)
    {
        var page = new RepositoryPage(Session, repo);
        page.Cloned += async (_, path) => await Main.AdoptClonedRepositoryAsync(path);
        AppNavigator.Show(page);
    }

    public static void EnsureGitHub()
    {
        if (GitHub is not null)
        {
            return;
        }

        GitHub = new GitHubPage(Session, Notifications);
        GitHub.Cloned += async (_, path) => await Main.AdoptClonedRepositoryAsync(path);
    }

    public static void ToggleCommandPalette()
    {
        var window = Application.Current?.Windows.FirstOrDefault();
        if (window?.Page is CommandPalettePage)
        {
            AppNavigator.GoBack();
            return;
        }

        AppNavigator.Show(new CommandPalettePage());
    }
}
