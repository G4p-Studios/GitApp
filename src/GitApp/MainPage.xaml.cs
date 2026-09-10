using System.Collections.ObjectModel;
using GitApp.Accessibility;
using GitApp.Domain;

namespace GitApp;

public sealed class MainViewModel
{
    // Placeholder until GitService lands in milestone 2.
    public ObservableCollection<Repo> Repos { get; } = new()
    {
        new Repo("gitapp", "main", 2, 0),
        new Repo("react-native-windows", "main", 0, 14),
        new Repo("nvda", "master", 0, 0),
        new Repo("dotfiles", "main", 1, 3),
    };
}

public partial class MainPage : ContentPage
{
    private readonly MainViewModel _vm = new();

    public MainPage()
    {
        InitializeComponent();
        BindingContext = _vm;

        // The list owns its pane's entry point, so F6 into the sidebar lands
        // on a row rather than on the pane wrapper.
        ReposPane.EntryControl = RepoList;
        DetailPane.EntryControl = FetchButton;

        // Remember which row had focus, so F6 back into the sidebar returns
        // the user to their place rather than to the top of the list.
        PlatformFocus.TrackFocusWithin(RepoList, "repos");

        Announcer.Current.StatusChanged += (_, text) =>
            Dispatcher.Dispatch(() => StatusLabel.Text = text);

        PaneNavigation.Attach(this);
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        // Put focus somewhere meaningful once the window is up. Deferred
        // because panes register as they load, and a page appears before its
        // children have finished doing so.
        Dispatcher.Dispatch(PaneNavigation.FocusFirstPane);
    }

    private void OnRepoSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not Repo repo)
        {
            return;
        }

        TitleLabel.Text = repo.Name;
        SubtitleLabel.Text = $"On branch {repo.Branch}, {repo.Sync}";

        FocusManager.Current.NoteFocusWithin("repos", RepoList);
    }

    private async void OnFetch(object? sender, EventArgs e)
    {
        if (RepoList.SelectedItem is not Repo repo)
        {
            return;
        }

        // The start/finish pairing is mandatory for anything that can outlast
        // a second: silence during a long Git operation reads as a hang.
        var done = Announcer.Current.Operation($"Fetching {repo.Name}");
        Announcer.Current.SetStatus($"Fetching {repo.Name}...");

        await Task.Delay(1200);

        done($"Fetched {repo.Name}, up to date");
    }

    private void OnNotImplemented(object? sender, EventArgs e)
    {
        var label = (sender as Button)?.Text ?? "That";
        Announcer.Current.Announce($"{label} is not implemented yet");
    }
}
