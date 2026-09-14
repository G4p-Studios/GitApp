using GitApp.Accessibility;
using GitApp.Domain;
using GitApp.GitHub;
using GitApp.Services;
using GitApp.ViewModels;

namespace GitApp;

public partial class GitHubPage : ContentPage
{
    private readonly GitHubSession _session;
    private readonly NotificationService _notifications;
    private readonly GitHubViewModel _vm;
    private NotificationsPage? _notificationsPage;
    private bool _initialised;

    public GitHubPage(GitHubSession session, NotificationService notifications)
    {
        InitializeComponent();

        _session = session;
        _notifications = notifications;
        _vm = new GitHubViewModel(session);
        BindingContext = _vm;

        AccountPane.EntryControl = TokenEntry;
        RemoteReposPane.EntryControl = RemoteRepoList;

        PlatformFocus.TrackFocusWithin(RemoteRepoList, "github-repos");
        PlatformFocus.TrackFocusWithin(TokenEntry, "github-account");
        PlatformFocus.DescribeEmptyView(RemoteRepoList, "No repositories to show");

        _vm.PickFolder = FolderPicker.PickAsync;

        _vm.Cloned += (_, path) => Cloned?.Invoke(this, path);
        _vm.OpenRequested += (_, repo) => ShowRepository(repo);
    }

    /// <summary>Raised so the main screen can add the new clone.</summary>
    public event EventHandler<string>? Cloned;

    private void OnStatusChanged(object? sender, string text) =>
        Dispatcher.Dispatch(() => StatusLabel.Text = text);

    protected override void OnAppearing()
    {
        base.OnAppearing();

        Announcer.Current.StatusChanged -= OnStatusChanged;
        Announcer.Current.StatusChanged += OnStatusChanged;

        PaneNavigation.Attach(this);
        PaneNavigation.BackHandler = () =>
        {
            GoBack();
            return true;
        };

        // Focus has to land on something, every time. A screen that
        // appears with focus on the window root leaves a screen reader
        // user with nothing read and nothing obvious to press.
        PaneNavigation.FocusFirstPaneWhenReady(this, announce: true);

        if (_initialised)
        {
            return;
        }

        _initialised = true;
        _ = _vm.InitialiseAsync();
    }

    protected override void OnDisappearing()
    {
        Announcer.Current.StatusChanged -= OnStatusChanged;
        base.OnDisappearing();
    }

    /// <summary>
    /// Escape backs out one level, the same as it does everywhere else in
    /// the app (ARCHITECTURE 3.4).
    /// </summary>
    protected override bool OnBackButtonPressed()
    {
        GoBack();
        return true;
    }

    private void OnBack(object? sender, EventArgs e) => GoBack();

    private static void GoBack()
    {
        AppNavigator.GoBack();
    }

    private async void OnCreateToken(object? sender, EventArgs e)
    {
        try
        {
            await Launcher.Default.OpenAsync("https://github.com/settings/tokens/new?scopes=repo,read:org,notifications&description=GitApp");
            Announcer.Current.Announce(
                "Opened the token page in your browser. Create the token, copy it, then come back and paste it here.");
        }
        catch (Exception)
        {
            Announcer.Current.Announce("Could not open your browser.");
        }
    }

    private async void OnBrowserSignIn(object? sender, EventArgs e) =>
        await _vm.StartBrowserSignInAsync();

    private void ShowRepository(GitHubRepository repo)
    {
        var page = new RepositoryPage(_session, repo);
        page.Cloned += (_, path) => Cloned?.Invoke(this, path);
        AppNavigator.Show(page);
    }

    /// <summary>
    /// Open the notifications inbox. Kept rather than rebuilt, so returning to
    /// it does not lose the user's place in the list.
    /// </summary>
    private void OnOpenNotifications(object? sender, EventArgs e) => ShowNotifications();

    /// <summary>Open the inbox and, when a toast sent us here, land on its row.</summary>
    public void ShowNotifications(string? threadId = null)
    {
        _notificationsPage ??= new NotificationsPage(_notifications);
        AppNavigator.Show(_notificationsPage);

        if (!string.IsNullOrEmpty(threadId))
        {
            _notificationsPage.FocusThread(threadId);
        }
    }
}
