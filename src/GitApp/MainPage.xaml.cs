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
        PlatformFocus.TrackFocusWithin(DiffList, "diff");

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
