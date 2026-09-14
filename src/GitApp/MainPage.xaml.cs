using GitApp.Accessibility;
using GitApp.Domain;
using GitApp.Services;
using GitApp.ViewModels;

namespace GitApp;

public partial class MainPage : ContentPage
{
    private readonly MainViewModel _vm;

    private bool _initialised;

    public MainPage()
    {
        InitializeComponent();
        _vm = AppHost.Main;
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
        PlatformFocus.TrackFocusWithin(StagedList, "changes");
        PlatformFocus.TrackFocusWithin(DiffList, "diff");
        PlatformFocus.TrackFocusWithin(CommitMessageEditor, "commit");

        DiffPane.EntryControl = DiffList;

        // An empty list still holds a tab stop, and MAUI leaves it with no
        // name and no automation peer, so tabbing onto it is silent. Give it
        // the same words the placeholder shows on screen.
        PlatformFocus.DescribeEmptyView(RepoList, "No repositories yet. Use Add repository to get started.");
        PlatformFocus.DescribeEmptyView(UnstagedList, "Nothing unstaged");
        PlatformFocus.DescribeEmptyView(StagedList, "Nothing staged");
        PlatformFocus.DescribeEmptyView(DiffList, "No file selected. Choose a changed file to see its differences.");
        PlatformFocus.DescribeEmptyView(HistoryList, "No commits");

        // The view model asks; the page owns the dialogs. Using the system
        // dialogs rather than custom ones means they are already keyboard
        // accessible and already familiar to screen reader users.
        _vm.PromptAsync = (title, message, initial) =>
            DisplayPromptAsync(title, message, "OK", "Cancel", initialValue: initial);

        _vm.ConfirmAsync = (title, message) =>
            DisplayAlertAsync(title, message, "Yes", "No");

        _vm.PickFolder = Services.FolderPicker.PickAsync;

        Announcer.Current.StatusChanged += (_, text) =>
            Dispatcher.Dispatch(() => StatusLabel.Text = text);

        PaneNavigation.Attach(this);
    }

    /// <summary>
    /// The keys this screen owns.
    ///
    /// Set on every appearance, because Attach clears the handlers so that
    /// one screen's keys cannot swallow another's.
    /// </summary>
    private void WireKeys()
    {
        // F7 moves between differences. Running out of them is the one case
        // that has to be announced, because nothing moves and silence after
        // a keypress reads as the key having done nothing.
        PaneNavigation.HunkNavigator = direction =>
        {
            if (_vm.DiffRows.Count == 0)
            {
                return false;
            }

            var from = DiffList.SelectedItem is DiffRow current
                ? _vm.DiffRows.IndexOf(current)
                : -1;

            var target = _vm.FindHunkRow(from, direction);
            if (target < 0)
            {
                Announcer.Current.Announce(
                    direction > 0 ? "No more differences" : "No previous differences");
                return true;
            }

            // Landing at the top leaves the rest of the difference below the
            // cursor, ready to read on.
            return MoveToDiffRow(
                target, _vm.DiffRows[target].AccessibleName, ScrollToPosition.Start);
        };

        // Folding a large difference. The header keeps its row index either
        // way, so focus stays exactly where it is and only the announcement
        // has to be made.
        PaneNavigation.RowExpander = open =>
        {
            if (FocusManager.Current.ActivePane?.Id != "diff"
                || DiffList.SelectedItem is not DiffRow row)
            {
                return false;
            }

            var index = _vm.DiffRows.IndexOf(row);

            // Left from inside a difference goes out to its header, the way
            // Left walks out of a subtree. Only then does another Left fold
            // it. Without the first step, escaping a four-hundred line block
            // means arrowing back up through all of it.
            if (!row.IsHeader)
            {
                if (open is not false)
                {
                    return false;
                }

                var header = _vm.FindHunkRow(index, -1);
                return header >= 0 && MoveToDiffRow(header, _vm.DiffRows[header].AccessibleName);
            }

            if (!row.IsExpandable || _vm.ToggleHunk(index, open) is not { } announcement)
            {
                return false;
            }

            return MoveToDiffRow(index, announcement);
        };
    }

    /// <summary>
    /// Select, scroll to and focus one diff row, announcing it only if focus
    /// did not actually land there.
    ///
    /// Selecting does not focus, so the focus call is always needed. The
    /// announcement almost never is: once focus moves, or the focused row's
    /// name changes underneath it, the screen reader reads the row itself,
    /// and announcing on top of that says the same thing twice. Listening
    /// through NVDA is the only way this shows up; the UI Automation tree
    /// looks identical either way.
    ///
    /// Deferred a frame because a row has no container to focus until the
    /// scroll has realized it.
    /// </summary>
    private bool MoveToDiffRow(
        int index,
        string fallbackAnnouncement,
        ScrollToPosition position = ScrollToPosition.MakeVisible)
    {
        DiffList.SelectedItem = _vm.DiffRows[index];
        DiffList.ScrollTo(index, position: position, animate: false);

        Dispatcher.Dispatch(() =>
        {
            if (!PlatformFocus.TryFocusSelectedItem(DiffList))
            {
                // Virtualization can leave the row without a container.
                // Nothing will be read, so say it.
                Announcer.Current.Announce(fallbackAnnouncement);
            }
        });

        return true;
    }

    /// <summary>
    /// Opening the GitHub repository list. Kept as a jump from this screen
    /// as well as from Home, so muscle memory from the old toolbar still
    /// works.
    /// </summary>
    private void OnOpenGitHub(object? sender, EventArgs e) => AppHost.ShowGitHub();

    private void OnHome(object? sender, EventArgs e) => AppHost.ShowHome();

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        // This screen owns the panes and the keys again.
        PaneNavigation.Attach(this);
        PaneNavigation.BackHandler = () =>
        {
            AppHost.ShowHome();
            return true;
        };
        WireKeys();

        if (_initialised)
        {
            // Coming back from the GitHub screen. Focus has to be placed
            // again: a page swap leaves it on an element that is no longer
            // shown, which reads as the app having gone silent.
            PaneNavigation.FocusFirstPaneWhenReady(this, announce: true);
            return;
        }

        _initialised = true;

        await _vm.InitialiseAsync();

        // Focus after the data is in place, so focus lands on a real row
        // rather than on an empty list. Deferred a frame because panes
        // register as their children load.
        PaneNavigation.FocusFirstPaneWhenReady(this);
    }
}
