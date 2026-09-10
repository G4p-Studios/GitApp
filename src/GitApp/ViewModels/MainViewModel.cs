using System.Collections.ObjectModel;
using System.Windows.Input;
using GitApp.Accessibility;
using GitApp.Domain;
using GitApp.Services;

namespace GitApp.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly GitService _git;
    private readonly RepositoryStore _store;
    private readonly Announcer _announcer;

    private RepositoryItem? _selectedRepository;
    private string _commitMessage = string.Empty;
    private bool _isBusy;

    public MainViewModel(GitService? git = null, RepositoryStore? store = null, Announcer? announcer = null)
    {
        _git = git ?? new GitService();
        _store = store ?? new RepositoryStore();
        _announcer = announcer ?? Announcer.Current;

        AddRepositoryCommand = new AsyncCommand(AddRepositoryAsync);
        RemoveRepositoryCommand = new AsyncCommand(RemoveRepositoryAsync, () => SelectedRepository is not null);
        RefreshCommand = new AsyncCommand(RefreshSelectedAsync, () => SelectedRepository is not null);
        StageAllCommand = new AsyncCommand(StageAllAsync, () => Unstaged.Count > 0);
        StageCommand = new AsyncCommand<FileChange>(StageAsync);
        UnstageCommand = new AsyncCommand<FileChange>(UnstageAsync);
        CommitCommand = new AsyncCommand(CommitAsync, CanCommit);
        FetchCommand = new AsyncCommand(FetchAsync, () => SelectedRepository is not null);
        PullCommand = new AsyncCommand(PullAsync, () => SelectedRepository is not null);
        PushCommand = new AsyncCommand(PushAsync, () => SelectedRepository is not null);
    }

    public ObservableCollection<RepositoryItem> Repositories { get; } = new();

    public ObservableCollection<FileChange> Staged { get; } = new();

    public ObservableCollection<FileChange> Unstaged { get; } = new();

    public ObservableCollection<CommitInfo> Commits { get; } = new();

    public RepositoryItem? SelectedRepository
    {
        get => _selectedRepository;
        set
        {
            if (Set(ref _selectedRepository, value))
            {
                Raise(nameof(HasSelection));
                Raise(nameof(BranchSummary));
                Raise(nameof(RepositoryTitle));
                RaiseCanExecuteChanged();
                _ = RefreshSelectedAsync();
            }
        }
    }

    public string CommitMessage
    {
        get => _commitMessage;
        set
        {
            if (Set(ref _commitMessage, value))
            {
                (CommitCommand as AsyncCommand)?.RaiseCanExecuteChanged();
            }
        }
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

    public bool HasSelection => SelectedRepository is not null;

    public string RepositoryTitle => SelectedRepository?.Name ?? "No repository selected";

    public string BranchSummary => SelectedRepository is { } repo
        ? $"On branch {repo.Status.Branch}, {repo.Status.SyncDescription}, {repo.Status.ChangeSummary}"
        : "Add a repository to get started";

    public ICommand AddRepositoryCommand { get; }
    public ICommand RemoveRepositoryCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand StageAllCommand { get; }
    public ICommand StageCommand { get; }
    public ICommand UnstageCommand { get; }
    public ICommand CommitCommand { get; }
    public ICommand FetchCommand { get; }
    public ICommand PullCommand { get; }
    public ICommand PushCommand { get; }

    // -----------------------------------------------------------------

    public async Task InitialiseAsync()
    {
        await _store.LoadAsync();

        foreach (var path in _store.Paths)
        {
            Repositories.Add(new RepositoryItem(path));
        }

        if (Repositories.Count > 0)
        {
            SelectedRepository = Repositories[0];
        }

        // Fill in branch and sync state for the rest without blocking, so the
        // list is usable immediately rather than after every repository has
        // been inspected.
        foreach (var item in Repositories.Skip(1))
        {
            item.Status = await _git.GetStatusAsync(item.Path);
        }
    }

    private async Task AddRepositoryAsync()
    {
        var folder = await PickFolderAsync();
        if (folder is null)
        {
            return;
        }

        var root = await _git.FindRepositoryRootAsync(folder);
        if (root is null)
        {
            _announcer.Announce($"{Path.GetFileName(folder)} is not a Git repository", Urgency.Assertive);
            return;
        }

        if (!await _store.AddAsync(root))
        {
            _announcer.Announce($"{Path.GetFileName(root)} is already in the list");
            return;
        }

        var item = new RepositoryItem(root);
        Repositories.Add(item);
        item.Status = await _git.GetStatusAsync(root);

        _announcer.Announce($"Added {item.Name}");
        SelectedRepository = item;
    }

    private async Task RemoveRepositoryAsync()
    {
        if (SelectedRepository is not { } repo)
        {
            return;
        }

        await _store.RemoveAsync(repo.Path);
        var index = Repositories.IndexOf(repo);
        Repositories.Remove(repo);

        _announcer.Announce($"Removed {repo.Name} from the list. The folder on disk was not touched.");

        // Keep focus somewhere sensible: the row that took the removed one's
        // place, or the new last row.
        SelectedRepository = Repositories.Count == 0
            ? null
            : Repositories[Math.Min(index, Repositories.Count - 1)];
    }

    public async Task RefreshSelectedAsync()
    {
        if (SelectedRepository is not { } repo)
        {
            Staged.Clear();
            Unstaged.Clear();
            Commits.Clear();
            return;
        }

        repo.Status = await _git.GetStatusAsync(repo.Path);

        Replace(Staged, repo.Status.Staged);
        Replace(Unstaged, repo.Status.Unstaged.Concat(repo.Status.Conflicted).ToList());
        Replace(Commits, await _git.GetLogAsync(repo.Path, 30));

        Raise(nameof(BranchSummary));
        RaiseCanExecuteChanged();
    }

    private async Task StageAllAsync()
    {
        if (SelectedRepository is not { } repo)
        {
            return;
        }

        await RunGitAsync(
            $"Staging all changes in {repo.Name}",
            () => _git.StageAllAsync(repo.Path),
            result => result.Success ? "Staged all changes" : $"Could not stage: {result.ErrorMessage}");
    }

    private async Task StageAsync(FileChange? change)
    {
        if (change is null || SelectedRepository is not { } repo)
        {
            return;
        }

        await RunGitAsync(
            $"Staging {change.FileName}",
            () => _git.StageAsync(repo.Path, new[] { change.Path }),
            result => result.Success
                ? $"Staged {change.FileName}"
                : $"Could not stage {change.FileName}: {result.ErrorMessage}");
    }

    private async Task UnstageAsync(FileChange? change)
    {
        if (change is null || SelectedRepository is not { } repo)
        {
            return;
        }

        await RunGitAsync(
            $"Unstaging {change.FileName}",
            () => _git.UnstageAsync(repo.Path, new[] { change.Path }),
            result => result.Success
                ? $"Unstaged {change.FileName}"
                : $"Could not unstage {change.FileName}: {result.ErrorMessage}");
    }

    private bool CanCommit() =>
        !IsBusy
        && SelectedRepository is { } repo
        && repo.Status.CanCommit
        && !string.IsNullOrWhiteSpace(CommitMessage);

    private async Task CommitAsync()
    {
        if (SelectedRepository is not { } repo)
        {
            return;
        }

        var message = CommitMessage.Trim();
        var count = Staged.Count;

        var ok = await RunGitAsync(
            $"Committing {count} {(count == 1 ? "file" : "files")} to {repo.Status.Branch}",
            () => _git.CommitAsync(repo.Path, message),
            result => result.Success
                ? $"Committed {count} {(count == 1 ? "file" : "files")} to {repo.Status.Branch}"
                : $"Commit failed: {result.ErrorMessage}");

        if (ok)
        {
            CommitMessage = string.Empty;
        }
    }

    private Task FetchAsync()
    {
        if (SelectedRepository is not { } repo)
        {
            return Task.CompletedTask;
        }

        return RunGitAsync(
            $"Fetching {repo.Name}",
            () => _git.FetchAsync(repo.Path),
            result => result.Success
                ? $"Fetched {repo.Name}, {repo.Status.SyncDescription}"
                : $"Fetch failed: {result.ErrorMessage}");
    }

    private Task PullAsync()
    {
        if (SelectedRepository is not { } repo)
        {
            return Task.CompletedTask;
        }

        return RunGitAsync(
            $"Pulling {repo.Name}",
            () => _git.PullAsync(repo.Path),
            result => result.Success
                ? $"Pulled {repo.Name}"
                // --ff-only means the common failure is a diverged branch,
                // which is worth saying plainly rather than echoing git.
                : result.ErrorMessage.Contains("non-fast-forward", StringComparison.OrdinalIgnoreCase)
                    || result.ErrorMessage.Contains("diverge", StringComparison.OrdinalIgnoreCase)
                        ? "Cannot pull: your branch and the remote have diverged. Merging is not supported yet."
                        : $"Pull failed: {result.ErrorMessage}");
    }

    private Task PushAsync()
    {
        if (SelectedRepository is not { } repo)
        {
            return Task.CompletedTask;
        }

        var branch = repo.Status.Branch;
        var needsUpstream = repo.Status.Upstream is null;

        return RunGitAsync(
            $"Pushing {branch}",
            () => needsUpstream
                ? _git.PushSetUpstreamAsync(repo.Path, branch)
                : _git.PushAsync(repo.Path),
            result => result.Success
                ? $"Pushed {branch}"
                : $"Push failed: {result.ErrorMessage}");
    }

    /// <summary>
    /// Run a git operation with the announcement pairing every long
    /// operation needs: say what is starting, say what happened, and never
    /// leave silence in between. See docs/ARCHITECTURE.md 3.5.
    /// </summary>
    private async Task<bool> RunGitAsync(
        string startMessage,
        Func<Task<GitResult>> operation,
        Func<GitResult, string> describe)
    {
        if (IsBusy)
        {
            return false;
        }

        IsBusy = true;
        if (SelectedRepository is { } busyRepo)
        {
            busyRepo.IsBusy = true;
        }

        var done = _announcer.Operation(startMessage);
        _announcer.SetStatus(startMessage + "...");

        try
        {
            var result = await operation();
            var message = describe(result);

            done(message);

            if (!result.Success)
            {
                // Errors interrupt: the user's next action depends on this.
                _announcer.Announce(message, Urgency.Assertive);
            }

            await RefreshSelectedAsync();
            return result.Success;
        }
        catch (Exception ex)
        {
            done($"{startMessage} failed: {ex.Message}");
            _announcer.Announce($"{startMessage} failed: {ex.Message}", Urgency.Assertive);
            return false;
        }
        finally
        {
            if (SelectedRepository is { } repo)
            {
                repo.IsBusy = false;
            }

            IsBusy = false;
        }
    }

    /// <summary>
    /// Update a bound collection in place.
    ///
    /// Clearing and refilling would be simpler, and would also destroy every
    /// item container, taking the focus position and the screen reader's
    /// place in the list with it. See docs/ARCHITECTURE.md 4.6.
    /// </summary>
    private static void Replace<T>(ObservableCollection<T> target, IReadOnlyList<T> source)
    {
        for (var i = 0; i < source.Count; i++)
        {
            if (i < target.Count)
            {
                if (!EqualityComparer<T>.Default.Equals(target[i], source[i]))
                {
                    target[i] = source[i];
                }
            }
            else
            {
                target.Add(source[i]);
            }
        }

        while (target.Count > source.Count)
        {
            target.RemoveAt(target.Count - 1);
        }
    }

    private void RaiseCanExecuteChanged()
    {
        foreach (var command in new[]
                 {
                     RemoveRepositoryCommand, RefreshCommand, StageAllCommand,
                     CommitCommand, FetchCommand, PullCommand, PushCommand,
                 })
        {
            (command as AsyncCommand)?.RaiseCanExecuteChanged();
        }
    }

    private static Task<string?> PickFolderAsync() => Services.FolderPicker.PickAsync();
}
