using System.Collections.ObjectModel;
using System.Windows.Input;
using GitApp.Accessibility;
using GitApp.Domain;
using GitApp.GitHub;
using GitApp.Services;

namespace GitApp.ViewModels;

/// <summary>
/// The GitHub screen: who you are signed in as, and what you can open.
/// </summary>
public sealed class GitHubViewModel : ObservableObject
{
    private readonly GitHubSession _session;
    private readonly Announcer _announcer;
    private readonly GitService _git;

    private string _token = string.Empty;
    private string _status = "Not signed in to GitHub";
    private string _listSummary = "Sign in to see your repositories";
    private bool _isBusy;
    private GitHubRepository? _selected;
    private string _filter = string.Empty;
    private IReadOnlyList<GitHubRepository> _all = Array.Empty<GitHubRepository>();
    private string _deviceCode = string.Empty;

    public GitHubViewModel(
        GitHubSession session,
        Announcer? announcer = null,
        GitService? git = null)
    {
        _session = session;
        _announcer = announcer ?? Announcer.Current;
        _git = git ?? new GitService();

        SignInCommand = new AsyncCommand(SignInAsync, () => !IsBusy);
        SignOutCommand = new AsyncCommand(SignOutAsync, () => IsSignedIn && !IsBusy);
        RefreshCommand = new AsyncCommand(LoadRepositoriesAsync, () => IsSignedIn && !IsBusy);
        CloneCommand = new AsyncCommand<GitHubRepository>(CloneAsync);
        OpenRepositoryCommand = new AsyncCommand<GitHubRepository>(OpenRepositoryAsync);
    }

    public ObservableCollection<GitHubRepository> Repositories { get; } = new();

    public ICommand SignInCommand { get; }

    public ICommand SignOutCommand { get; }

    public ICommand RefreshCommand { get; }

    public ICommand CloneCommand { get; }

    public ICommand OpenRepositoryCommand { get; }

    /// <summary>Set by the page, which owns the folder picker.</summary>
    public Func<Task<string?>>? PickFolder { get; set; }

    public bool IsSignedIn => _session.IsSignedIn;

    public bool IsSignedOut => !_session.IsSignedIn;

    /// <summary>
    /// Browser sign-in is hidden rather than disabled when there is no
    /// client ID. A permanently greyed-out button is a puzzle a screen
    /// reader user has to solve by reading a hint that may not be spoken.
    /// </summary>
    public bool CanUseBrowserSignIn => _session.CanUseBrowserSignIn;

    /// <summary>
    /// The "go here and type this" instruction, while browser sign-in is
    /// running.
    ///
    /// On the page rather than in a dialog, deliberately. A modal would
    /// have to be dismissed before the user could reach their browser, and
    /// it could not be closed from underneath them when the token arrives.
    /// Here the code stays readable for as long as it is needed, and can be
    /// re-read at any point without restarting anything.
    /// </summary>
    public string DeviceCodeMessage
    {
        get => _deviceCode;
        private set
        {
            if (Set(ref _deviceCode, value))
            {
                Raise(nameof(HasDeviceCode));
            }
        }
    }

    public bool HasDeviceCode => !string.IsNullOrEmpty(_deviceCode);

    /// <summary>
    /// OAuth device flow. The browser is the user's own, with their own
    /// screen reader setup, rather than a web view embedded in this app.
    /// </summary>
    public async Task StartBrowserSignInAsync()
    {
        if (_session.DeviceFlowClientId is not { Length: > 0 } clientId)
        {
            _announcer.Announce(
                "Browser sign-in is not configured. Use a personal access token.");
            return;
        }

        IsBusy = true;

        try
        {
            using var flow = new DeviceFlow(clientId);

            var started = await flow.StartAsync();
            if (!started.Success)
            {
                _announcer.Announce(started.Error!, Urgency.Assertive);
                return;
            }

            var code = started.Value!;
            DeviceCodeMessage = code.AccessibleName;

            // The clipboard saves retyping a code that a screen reader has
            // to spell out one character at a time.
            try
            {
                await Clipboard.Default.SetTextAsync(code.UserCode);
                _announcer.Announce(
                    $"{code.AccessibleName}. The code is on your clipboard.", Urgency.Assertive);
            }
            catch (Exception)
            {
                _announcer.Announce(code.AccessibleName, Urgency.Assertive);
            }

            try
            {
                await Launcher.Default.OpenAsync(code.VerificationUri);
            }
            catch (Exception)
            {
                // The code and the address were just announced, so the
                // user can get there without us.
            }

            var token = await flow.WaitForTokenAsync(
                code,
                remaining => _announcer.SetStatus(
                    $"Waiting for you to finish in the browser. {(int)remaining.TotalMinutes} minutes left."));

            _announcer.ClearStatus();
            DeviceCodeMessage = string.Empty;

            if (!token.Success)
            {
                _announcer.Announce(token.Error!, Urgency.Assertive);
                return;
            }

            var signedIn = await _session.SignInWithTokenAsync(token.Value!);

            Status = _session.StatusDescription;
            RaiseSignedInState();
            _announcer.Announce(signedIn.Success ? _session.StatusDescription : signedIn.Error!);

            if (signedIn.Success)
            {
                await LoadRepositoriesAsync(announceStart: false);
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    public string Token
    {
        get => _token;
        set => Set(ref _token, value);
    }

    public string Status
    {
        get => _status;
        private set => Set(ref _status, value);
    }

    public string ListSummary
    {
        get => _listSummary;
        private set => Set(ref _listSummary, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (Set(ref _isBusy, value))
            {
                RaiseCanExecuteChanged();
            }
        }
    }

    public GitHubRepository? SelectedRepository
    {
        get => _selected;
        set => Set(ref _selected, value);
    }

    /// <summary>
    /// Typing narrows the list. With a few hundred repositories, arrowing
    /// is not a way to find one, and this is the equivalent of the search
    /// box on github.com.
    /// </summary>
    public string Filter
    {
        get => _filter;
        set
        {
            if (Set(ref _filter, value))
            {
                ApplyFilter(announce: true);
            }
        }
    }

    public async Task InitialiseAsync()
    {
        var restored = await _session.RestoreAsync();

        Status = _session.StatusDescription;
        RaiseSignedInState();

        if (restored.Success)
        {
            await LoadRepositoriesAsync();
        }
        else if (restored.Failure == GitHubFailure.Offline)
        {
            // Worth saying: signed in, but the list is empty for a reason
            // that has nothing to do with the account.
            ListSummary = restored.Error!;
            _announcer.Announce(restored.Error!, Urgency.Assertive);
        }
    }

    private async Task SignInAsync()
    {
        IsBusy = true;
        var done = _announcer.Operation("Signing in to GitHub");

        try
        {
            var result = await _session.SignInWithTokenAsync(Token);

            if (!result.Success)
            {
                done(result.Error!);
                return;
            }

            // The token has served its purpose and is in the credential
            // store now. Holding it in a bound property keeps it in the
            // visual tree, where anything that dumps UI state would catch
            // it.
            Token = string.Empty;

            Status = _session.StatusDescription;
            RaiseSignedInState();
            done(_session.StatusDescription);

            await LoadRepositoriesAsync(announceStart: false);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task SignOutAsync()
    {
        await _session.SignOutAsync();

        Repositories.Clear();
        _all = Array.Empty<GitHubRepository>();
        Status = _session.StatusDescription;
        ListSummary = "Sign in to see your repositories";
        RaiseSignedInState();

        _announcer.Announce("Signed out of GitHub");
    }

    private Task LoadRepositoriesAsync() => LoadRepositoriesAsync(announceStart: true);

    private async Task LoadRepositoriesAsync(bool announceStart)
    {
        if (_session.CreateClient() is not { } client)
        {
            return;
        }

        IsBusy = true;
        var done = announceStart
            ? _announcer.Operation("Loading your repositories")
            : new Action<string>(message => _announcer.Announce(message));

        try
        {
            var result = await client.GetRepositoriesAsync(
                new Progress<int>(count => _announcer.SetStatus($"Loaded {count} repositories")));

            if (!result.Success)
            {
                ListSummary = result.Error!;
                done(result.Error!);

                if (result.Failure == GitHubFailure.Unauthenticated)
                {
                    await SignOutAsync();
                }

                return;
            }

            _all = result.Value!;
            ApplyFilter(announce: false);

            done(ListSummary);
        }
        finally
        {
            client.Dispose();
            _announcer.ClearStatus();
            IsBusy = false;
        }
    }

    private void ApplyFilter(bool announce)
    {
        var needle = _filter.Trim();

        var matches = string.IsNullOrEmpty(needle)
            ? _all
            : _all.Where(r =>
                r.Name.Contains(needle, StringComparison.OrdinalIgnoreCase)
                || r.Owner.Contains(needle, StringComparison.OrdinalIgnoreCase)
                || (r.Description?.Contains(needle, StringComparison.OrdinalIgnoreCase) ?? false))
                .ToList();

        Repositories.Clear();
        foreach (var repo in matches)
        {
            Repositories.Add(repo);
        }

        ListSummary = Describe(matches.Count, needle);

        if (announce)
        {
            // The count is the whole point of typing: the user needs to
            // know whether to keep narrowing or start arrowing.
            _announcer.Announce(ListSummary);
        }
    }

    private string Describe(int count, string needle)
    {
        if (!IsSignedIn)
        {
            return "Sign in to see your repositories";
        }

        var noun = count == 1 ? "1 repository" : $"{count} repositories";

        return string.IsNullOrEmpty(needle)
            ? $"{noun}"
            : count == 0
                ? $"No repositories match {needle}"
                : $"{noun} matching {needle}";
    }

    private async Task CloneAsync(GitHubRepository? repo)
    {
        if (repo is null || PickFolder is null)
        {
            return;
        }

        var parent = await PickFolder();
        if (string.IsNullOrEmpty(parent))
        {
            _announcer.Announce("Clone cancelled");
            return;
        }

        var done = _announcer.Operation($"Cloning {repo.FullName}");

        var (result, path) = await _git.CloneAsync(
            repo.CloneUrl,
            parent,
            progress => _announcer.SetStatus(progress));

        _announcer.ClearStatus();

        if (!result.Success)
        {
            done($"Clone of {repo.Name} failed. {result.ErrorMessage}");
            return;
        }

        done($"Cloned {repo.Name} into {path}");
        Cloned?.Invoke(this, path!);
    }

    /// <summary>
    /// Raised after a successful clone so the main screen can add it to the
    /// local list. A repository cloned but not listed is a repository the
    /// user has to go and find by hand.
    /// </summary>
    public event EventHandler<string>? Cloned;

    /// <summary>Open the in-app repository view, not github.com.</summary>
    public event EventHandler<GitHubRepository>? OpenRequested;

    private Task OpenRepositoryAsync(GitHubRepository? repo)
    {
        if (repo is not null)
        {
            OpenRequested?.Invoke(this, repo);
        }

        return Task.CompletedTask;
    }

    private void RaiseSignedInState()
    {
        Raise(nameof(IsSignedIn));
        Raise(nameof(IsSignedOut));
        RaiseCanExecuteChanged();
    }

    private void RaiseCanExecuteChanged()
    {
        (SignInCommand as AsyncCommand)?.RaiseCanExecuteChanged();
        (SignOutCommand as AsyncCommand)?.RaiseCanExecuteChanged();
        (RefreshCommand as AsyncCommand)?.RaiseCanExecuteChanged();
    }
}
