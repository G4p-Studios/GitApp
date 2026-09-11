using GitApp.Accessibility;
using GitApp.Domain;
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
        PlatformFocus.TrackFocusWithin(StagedList, "changes");
        PlatformFocus.TrackFocusWithin(DiffList, "diff");
        PlatformFocus.TrackFocusWithin(CommitMessageEditor, "commit");

        DiffPane.EntryControl = DiffList;

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

        // F7 moves between differences. Announcing the landing row is
        // deliberate: the list scrolls and selects, but a selection change
        // alone is not always spoken, and silence after a keypress reads as
        // the key having done nothing.
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

            var row = _vm.DiffRows[target];
            DiffList.SelectedItem = row;
            DiffList.ScrollTo(target, position: ScrollToPosition.Start, animate: false);

            // Selecting does not focus. Without moving focus too, F7 would
            // announce the destination and leave the user behind, unable to
            // read on from where they landed. Deferred a frame so the row is
            // realized after the scroll before we try to focus it.
            Dispatcher.Dispatch(() =>
            {
                PlatformFocus.TryFocusSelectedItem(DiffList);
                Announcer.Current.Announce(row.AccessibleName);
            });

            return true;
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

        PaneNavigation.Attach(this);
    }

    /// <summary>
    /// Select, scroll to, focus and announce one diff row.
    ///
    /// Selecting does not focus, and focusing without saying anything reads
    /// as the key having done nothing. Both halves are needed every time, so
    /// they live together. Deferred a frame because a row has no container
    /// to focus until the scroll has realized it.
    /// </summary>
    private bool MoveToDiffRow(int index, string announcement)
    {
        DiffList.SelectedItem = _vm.DiffRows[index];
        DiffList.ScrollTo(index, position: ScrollToPosition.MakeVisible, animate: false);

        Dispatcher.Dispatch(() =>
        {
            PlatformFocus.TryFocusSelectedItem(DiffList);
            Announcer.Current.Announce(announcement);
        });

        return true;
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
