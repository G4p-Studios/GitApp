using GitApp.Accessibility;
using GitApp.ViewModels;

namespace GitApp;

public partial class MainPage : ContentPage
{
    private readonly MainViewModel _vm = new();

    public MainPage()
    {
        InitializeComponent();
        BindingContext = _vm;

        // Each pane hands focus to its most useful control, so F6 lands on
        // something actionable rather than on the pane wrapper.
        ReposPane.EntryControl = RepoList;
        ChangesPane.EntryControl = UnstagedList;
        CommitPane.EntryControl = CommitMessageEditor;

        // Remember which row had focus in each list, so F6 back returns the
        // user to their place instead of the top.
        PlatformFocus.TrackFocusWithin(RepoList, "repos");
        PlatformFocus.TrackFocusWithin(UnstagedList, "changes");

        Announcer.Current.StatusChanged += (_, text) =>
            Dispatcher.Dispatch(() => StatusLabel.Text = text);

        PaneNavigation.Attach(this);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        await _vm.InitialiseAsync();

        // Focus after the data is in place, so focus lands on a real row
        // rather than on an empty list. Deferred a frame because panes
        // register as their children load.
        Dispatcher.Dispatch(PaneNavigation.FocusFirstPane);
    }
}
