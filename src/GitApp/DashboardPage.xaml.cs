using GitApp.Accessibility;
using GitApp.Services;
using GitApp.ViewModels;

namespace GitApp;

public partial class DashboardPage : ContentPage
{
    private readonly DashboardViewModel _vm;
    private bool _initialised;

    public DashboardPage()
    {
        InitializeComponent();

        _vm = new DashboardViewModel();
        BindingContext = _vm;

        PlacesPane.EntryControl = PlacesList;
        TopPane.EntryControl = TopList;
        FeedPane.EntryControl = TokenEntry;
        TopPane.IsVisible = false;

        PlatformFocus.TrackFocusWithin(PlacesList, "home-places");
        PlatformFocus.TrackFocusWithin(TopList, "home-top");
        PlatformFocus.TrackFocusWithin(FeedList, "home-feed");
        PlatformFocus.DescribeEmptyView(TopList, "No repositories to jump to");
        PlatformFocus.DescribeEmptyView(FeedList, "No activity to show");

        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(DashboardViewModel.IsSignedIn))
            {
                Dispatcher.Dispatch(ApplySignedIn);
            }
        };
    }

    /// <summary>
    /// Palette sign-out (and GitHub-page sign-in) change the session
    /// without going through this view model. Re-read it when Home is
    /// shown again.
    /// </summary>
    public void RefreshSession() => _ = _vm.SyncSessionAsync();

    protected override void OnAppearing()
    {
        base.OnAppearing();

        Announcer.Current.StatusChanged -= OnStatusChanged;
        Announcer.Current.StatusChanged += OnStatusChanged;

        AppHost.Main.PickFolder = FolderPicker.PickAsync;
        AppHost.Main.PromptAsync = (title, message, initial) =>
            DisplayPromptAsync(title, message, "OK", "Cancel", initialValue: initial);
        AppHost.Main.ConfirmAsync = (title, message) =>
            DisplayAlertAsync(title, message, "Yes", "No");

        PaneNavigation.Attach(this);
        PaneNavigation.ActivateHandler = TryActivate;

        AppHost.Notifications.OpenRequested -= OnToast;
        AppHost.Notifications.OpenRequested += OnToast;

        if (_initialised)
        {
            _ = _vm.SyncSessionAsync();
            Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(200), RestorePlacesFocus);
            return;
        }

        _initialised = true;
        _ = _vm.InitialiseAsync();
        PaneNavigation.FocusFirstPaneWhenReady(this, announce: true);
    }

    /// <summary>
    /// Returning to Home must land on the selected place, not the first
    /// row. FocusFirstPane takes the first descendant, and Enter follows
    /// SelectedPlace, so the two disagreeing means Enter opens Local while
    /// NVDA is still saying Home.
    /// </summary>
    private void RestorePlacesFocus()
    {
        if (PlatformFocus.TryFocusSelectedItem(PlacesList))
        {
            FocusManager.Current.NoteActivePane("home-places");
            Announcer.Current.Announce("Places pane");
            return;
        }

        PaneNavigation.FocusFirstPane(announce: true);
    }

    private void ApplySignedIn()
    {
        var signedIn = _vm.IsSignedIn;
        TopPane.IsVisible = signedIn;
        FeedPane.EntryControl = signedIn ? FeedList : TokenEntry;
    }

    protected override void OnDisappearing()
    {
        Announcer.Current.StatusChanged -= OnStatusChanged;
        base.OnDisappearing();
    }

    private void OnStatusChanged(object? sender, string text) =>
        Dispatcher.Dispatch(() => StatusLabel.Text = text);

    private void OnToast(object? sender, string threadId) =>
        Dispatcher.Dispatch(() => AppHost.ShowNotifications(threadId));

    private void OnNotifications(object? sender, EventArgs e) =>
        AppHost.ShowNotifications();

    private void OnCommands(object? sender, EventArgs e) =>
        AppHost.ToggleCommandPalette();

    private async void OnCreateToken(object? sender, EventArgs e)
    {
        try
        {
            await Launcher.Default.OpenAsync(
                "https://github.com/settings/tokens/new?scopes=repo,read:org,notifications&description=GitApp");
            Announcer.Current.Announce(
                "Opened the token page in your browser. Create the token, copy it, then come back and paste it here.");
        }
        catch (Exception)
        {
            Announcer.Current.Announce("Could not open your browser.");
        }
    }

    private bool TryActivate()
    {
        return FocusManager.Current.ActivePane?.Id switch
        {
            "home-places" => _vm.OpenSelectedPlace(),
            "home-top" => _vm.OpenSelectedTop(),
            "home-feed" when _vm.IsSignedIn => _vm.OpenSelectedFeed(),
            _ => false,
        };
    }
}
