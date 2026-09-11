using GitApp.Accessibility;
using GitApp.Domain;
using GitApp.GitHub;
using GitApp.Services;
using GitApp.ViewModels;

namespace GitApp;

public partial class WorkListPage : ContentPage
{
    private readonly GitHubSession _session;
    private readonly GitHubRepository _listed;
    private readonly WorkListViewModel _vm;
    private bool _initialised;

    public WorkListPage(GitHubSession session, GitHubRepository listed, GitHubWorkKind kind)
    {
        InitializeComponent();

        _session = session;
        _listed = listed;
        _vm = new WorkListViewModel(session, listed, kind);
        BindingContext = _vm;
        Title = _vm.Heading;

        FilterPane.EntryControl = StatePicker;
        ListPane.EntryControl = WorkList;

        PlatformFocus.TrackFocusWithin(WorkList, "work-list");
        PlatformFocus.TrackFocusWithin(StatePicker, "work-filter");
        PlatformFocus.TrackFocusWithin(FilterEntry, "work-filter");
        PlatformFocus.DescribeEmptyView(WorkList, "Nothing to show");

        _vm.OpenRequested += (_, item) => ShowDetail(item);
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        Announcer.Current.StatusChanged -= OnStatusChanged;
        Announcer.Current.StatusChanged += OnStatusChanged;

        PaneNavigation.Attach(this);
        PaneNavigation.BackHandler = HandleBack;
        PaneNavigation.ActivateHandler = TryActivate;

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

    protected override bool OnBackButtonPressed()
    {
        HandleBack();
        return true;
    }

    private void OnBack(object? sender, EventArgs e) => HandleBack();

    private void OnStatusChanged(object? sender, string text) =>
        Dispatcher.Dispatch(() => StatusLabel.Text = text);

    private bool HandleBack()
    {
        if (AppNavigator.GoBack())
        {
            Announcer.Current.Announce(_listed.FullName);
            return true;
        }

        return false;
    }

    private bool TryActivate()
    {
        if (FocusManager.Current.ActivePane?.Id != "work-list"
            || StatePicker.IsFocused
            || FilterEntry.IsFocused
            || BackButton.IsFocused
            || RefreshButton.IsFocused)
        {
            return false;
        }

        return _vm.TryOpenSelected();
    }

    private void ShowDetail(GitHubWorkItem item)
    {
        AppNavigator.Show(new WorkDetailPage(_session, _listed, item));
    }
}
