using System.Collections.ObjectModel;
using System.Windows.Input;
using GitApp.Accessibility;
using GitApp.Domain;
using GitApp.GitHub;
using GitApp.Services;

namespace GitApp.ViewModels;

/// <summary>
/// Home: a github.com-style dashboard. Places on the left, the feed in the
/// middle. Signing in is automatic when a token is already stored.
/// </summary>
public sealed class DashboardViewModel : ObservableObject
{
    private readonly Announcer _announcer;

    private string _status = "Signing in to GitHub";
    private string _feedHeading = "Feed";
    private string _token = string.Empty;
    private bool _isBusy;
    private bool _signedIn;
    private DashboardPlace? _selectedPlace;
    private GitHubRepository? _selectedTop;
    private ActivityEvent? _selectedEvent;

    public DashboardViewModel(Announcer? announcer = null)
    {
        _announcer = announcer ?? Announcer.Current;

        Places.Add(new DashboardPlace("home", "Home", "Your GitHub feed"));
        Places.Add(new DashboardPlace("local", "Local repositories", "Repositories on this computer"));
        Places.Add(new DashboardPlace("remotes", "Your repositories", "Repositories on GitHub"));
        Places.Add(new DashboardPlace("notifications", "Notifications", "Unread GitHub notifications"));

        _selectedPlace = Places[0];

        RefreshCommand = new AsyncCommand(LoadAsync, () => !IsBusy);
        SignInCommand = new AsyncCommand(SignInAsync, () => !IsBusy);
        SignOutCommand = new AsyncCommand(SignOutAsync, () => IsSignedIn && !IsBusy);
        OpenPlaceCommand = new AsyncCommand<DashboardPlace>(p =>
        {
            OpenPlace(p);
            return Task.CompletedTask;
        });
        OpenTopCommand = new AsyncCommand<GitHubRepository>(OpenTopAsync);
        OpenFeedCommand = new AsyncCommand<ActivityEvent>(OpenFeedAsync);
    }

    public ObservableCollection<DashboardPlace> Places { get; } = new();

    public ObservableCollection<GitHubRepository> TopRepositories { get; } = new();

    public ObservableCollection<ActivityEvent> Feed { get; } = new();

    public ICommand RefreshCommand { get; }

    public ICommand SignInCommand { get; }

    public ICommand SignOutCommand { get; }

    public ICommand OpenPlaceCommand { get; }

    public ICommand OpenTopCommand { get; }

    public ICommand OpenFeedCommand { get; }

    public string Status
    {
        get => _status;
        private set => Set(ref _status, value);
    }

    public string FeedHeading
    {
        get => _feedHeading;
        private set => Set(ref _feedHeading, value);
    }

    public string Token
    {
        get => _token;
        set => Set(ref _token, value);
    }

    public bool IsSignedIn
    {
        get => _signedIn;
        private set
        {
            if (Set(ref _signedIn, value))
            {
                Raise(nameof(IsSignedOut));
                (SignOutCommand as AsyncCommand)?.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsSignedOut => !IsSignedIn;

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (Set(ref _isBusy, value))
            {
                (RefreshCommand as AsyncCommand)?.RaiseCanExecuteChanged();
                (SignInCommand as AsyncCommand)?.RaiseCanExecuteChanged();
                (SignOutCommand as AsyncCommand)?.RaiseCanExecuteChanged();
            }
        }
    }

    public DashboardPlace? SelectedPlace
    {
        get => _selectedPlace;
        set => Set(ref _selectedPlace, value);
    }

    public GitHubRepository? SelectedTop
    {
        get => _selectedTop;
        set => Set(ref _selectedTop, value);
    }

    public ActivityEvent? SelectedEvent
    {
        get => _selectedEvent;
        set => Set(ref _selectedEvent, value);
    }

    public async Task InitialiseAsync()
    {
        await AppHost.Main.InitialiseHostAsync();
        await LoadAsync();
        _ = AppHost.Main.InitialiseAsync();
    }

    public Task SyncSessionAsync() =>
        AppHost.Session.IsSignedIn == IsSignedIn
            ? Task.CompletedTask
            : LoadAsync();

    public bool OpenPlace(DashboardPlace? place)
    {
        if (place is null)
        {
            return false;
        }

        SelectedPlace = place;

        switch (place.Id)
        {
            case "home":
                return true;
            case "local":
                AppHost.ShowLocal();
                return true;
            case "remotes":
                AppHost.ShowGitHub();
                return true;
            case "notifications":
                AppHost.ShowNotifications();
                return true;
            default:
                return false;
        }
    }

    public bool OpenSelectedPlace() => OpenPlace(SelectedPlace);

    public bool OpenSelectedTop()
    {
        if (SelectedTop is not { } repo)
        {
            return false;
        }

        AppHost.ShowRepository(repo);
        return true;
    }

    public bool OpenSelectedFeed()
    {
        if (SelectedEvent is not { } ev)
        {
            return false;
        }

        _ = OpenFeedAsync(ev);
        return true;
    }

    private async Task LoadAsync()
    {
        IsBusy = true;
        var done = _announcer.Operation("Loading your GitHub home");

        try
        {
            var session = AppHost.Session;
            var restored = session.IsSignedIn
                ? GitHubResult<GitHubAccount>.Ok(session.Account!)
                : await session.RestoreAsync();

            IsSignedIn = restored.Success;
            Status = session.StatusDescription;

            if (!restored.Success)
            {
                Feed.Clear();
                TopRepositories.Clear();
                SelectedTop = null;
                SelectedEvent = null;
                FeedHeading = "Sign in to see your feed";
                done(restored.Failure == GitHubFailure.Unauthenticated
                    ? "Not signed in to GitHub"
                    : restored.Error!);
                return;
            }

            if (session.CreateClient() is not { } client)
            {
                done("Not signed in to GitHub");
                return;
            }

            try
            {
                var login = session.Account!.Login;
                var top = await client.GetRecentRepositoriesAsync();
                var feed = await client.GetReceivedEventsAsync(login);

                TopRepositories.Clear();
                if (top.Success)
                {
                    foreach (var repo in top.Value!)
                    {
                        TopRepositories.Add(repo);
                    }
                }

                Feed.Clear();
                if (feed.Success)
                {
                    foreach (var ev in feed.Value!)
                    {
                        Feed.Add(ev);
                    }
                }

                FeedHeading = Feed.Count switch
                {
                    0 => "No activity to show",
                    1 => "1 event",
                    _ => $"{Feed.Count} events",
                };

                SelectedTop = TopRepositories.FirstOrDefault();
                SelectedEvent = Feed.FirstOrDefault();

                if (!top.Success)
                {
                    done(top.Error!);
                    return;
                }

                if (!feed.Success)
                {
                    done(feed.Error!);
                    return;
                }

                done(FeedHeading);
            }
            finally
            {
                client.Dispose();
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task SignInAsync()
    {
        IsBusy = true;
        var done = _announcer.Operation("Signing in to GitHub");

        try
        {
            var result = await AppHost.Session.SignInWithTokenAsync(Token);
            if (!result.Success)
            {
                done(result.Error!);
                return;
            }

            Token = string.Empty;
            IsSignedIn = true;
            Status = AppHost.Session.StatusDescription;
            done(Status);
            await LoadAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task SignOutAsync()
    {
        await AppHost.Session.SignOutAsync();
        IsSignedIn = false;
        Status = AppHost.Session.StatusDescription;
        Feed.Clear();
        TopRepositories.Clear();
        SelectedTop = null;
        SelectedEvent = null;
        FeedHeading = "Sign in to see your feed";
        _announcer.Announce("Signed out of GitHub");
    }

    private Task OpenTopAsync(GitHubRepository? repo)
    {
        if (repo is not null)
        {
            AppHost.ShowRepository(repo);
        }

        return Task.CompletedTask;
    }

    private async Task OpenFeedAsync(ActivityEvent? ev)
    {
        if (ev is null)
        {
            return;
        }

        if (ev.Owner is { Length: > 0 } owner && ev.RepoName is { Length: > 0 } name)
        {
            AppHost.ShowRepository(new GitHubRepository(
                name, owner, null, false, false, null, 0, ev.CreatedAt,
                $"https://github.com/{owner}/{name}.git",
                ev.HtmlUrl ?? $"https://github.com/{owner}/{name}"));
            return;
        }

        if (ev.HtmlUrl is not { Length: > 0 } url)
        {
            return;
        }

        try
        {
            await Launcher.Default.OpenAsync(url);
            _announcer.Announce($"Opened {ev.Summary} in your browser");
        }
        catch (Exception)
        {
            _announcer.Announce("Could not open your browser.");
        }
    }
}

public sealed record DashboardPlace(string Id, string Title, string Hint)
{
    public string AccessibleName => $"{Title}, {Hint}";
}
