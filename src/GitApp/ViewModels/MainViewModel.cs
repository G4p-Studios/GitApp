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
    private BranchInfo? _selectedBranch;
    private bool _isMerging;
    private string _oursLabel = "this branch";
    private string _theirsLabel = "the other branch";

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

        CloneCommand = new AsyncCommand(CloneAsync);
        SwitchBranchCommand = new AsyncCommand<BranchInfo>(SwitchBranchAsync);
        CreateBranchCommand = new AsyncCommand(CreateBranchAsync, () => SelectedRepository is not null);
        AbortMergeCommand = new AsyncCommand(AbortMergeAsync, () => IsMerging);
        KeepOursCommand = new AsyncCommand<FileChange>(c => ResolveAsync(c, ConflictSide.Ours));
        KeepTheirsCommand = new AsyncCommand<FileChange>(c => ResolveAsync(c, ConflictSide.Theirs));
        MarkResolvedCommand = new AsyncCommand<FileChange>(MarkResolvedAsync);
    }

    public ObservableCollection<RepositoryItem> Repositories { get; } = new();

    public ObservableCollection<FileChange> Staged { get; } = new();

    public ObservableCollection<FileChange> Unstaged { get; } = new();

    public ObservableCollection<CommitInfo> Commits { get; } = new();

    public ObservableCollection<BranchInfo> Branches { get; } = new();

    public ObservableCollection<FileChange> Conflicts { get; } = new();

    /// <summary>
    /// Bound to the branch picker. Setting it switches branches, so the
    /// setter guards against the assignment that happens when the list is
    /// merely refreshed.
    /// </summary>
    public BranchInfo? SelectedBranch
    {
        get => _selectedBranch;
        set
        {
            var previous = _selectedBranch;
            if (!Set(ref _selectedBranch, value))
            {
                return;
            }

            if (value is not null && !value.IsCurrent && previous is not null)
            {
                _ = SwitchBranchAsync(value);
            }
        }
    }

    public bool IsMerging
    {
        get => _isMerging;
        private set
        {
            if (Set(ref _isMerging, value))
            {
                Raise(nameof(MergeSummary));
                RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>
    /// Names the two sides of a conflict by branch rather than as "ours" and
    /// "theirs", which are ambiguous even to people who use git daily.
    /// </summary>
    public string KeepOursLabel => $"Keep {_oursLabel}";

    public string KeepTheirsLabel => $"Keep {_theirsLabel}";

    public string MergeSummary => IsMerging
        ? $"Merge in progress: {Conflicts.Count} file{(Conflicts.Count == 1 ? string.Empty : "s")} to resolve"
        : string.Empty;

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
    public ICommand CloneCommand { get; }
    public ICommand SwitchBranchCommand { get; }
    public ICommand CreateBranchCommand { get; }
    public ICommand AbortMergeCommand { get; }
    public ICommand KeepOursCommand { get; }
    public ICommand KeepTheirsCommand { get; }
    public ICommand MarkResolvedCommand { get; }

    /// <summary>
    /// Asks the user something. Set by the page, because a view model has no
    /// business owning a dialog. Returns null when cancelled.
    /// </summary>
    public Func<string, string, string, Task<string?>>? PromptAsync { get; set; }

    /// <summary>Asks the user to confirm. Set by the page.</summary>
    public Func<string, string, Task<bool>>? ConfirmAsync { get; set; }

    /// <summary>Asks the user to choose a folder. Set by the page.</summary>
    public Func<Task<string?>>? PickFolder { get; set; }

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
        Replace(Unstaged, repo.Status.Unstaged);
        Replace(Conflicts, repo.Status.Conflicted);
        Replace(Commits, await _git.GetLogAsync(repo.Path, 30));
        Replace(Branches, await _git.GetBranchDetailsAsync(repo.Path));

        // Assign through the field so the setter does not read a refresh as
        // a request to switch branches.
        _selectedBranch = Branches.FirstOrDefault(b => b.IsCurrent);
        Raise(nameof(SelectedBranch));

        IsMerging = await _git.IsMergeInProgressAsync(repo.Path);
        if (IsMerging)
        {
            (_oursLabel, _theirsLabel) = await _git.GetMergeSidesAsync(repo.Path);
            Raise(nameof(KeepOursLabel));
            Raise(nameof(KeepTheirsLabel));
        }

        Raise(nameof(BranchSummary));
        Raise(nameof(MergeSummary));
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

    private async Task PullAsync()
    {
        if (SelectedRepository is not { } repo)
        {
            return;
        }

        // Fast-forward first. It cannot conflict, so it never leaves the user
        // somewhere they did not ask to be.
        var fastForwarded = await RunGitAsync(
            $"Pulling {repo.Name}",
            () => _git.PullAsync(repo.Path),
            result => result.Success
                ? $"Pulled {repo.Name}"
                // --ff-only means the common failure is a diverged branch,
                // which is worth saying plainly rather than echoing git.
                : $"Pull failed: {result.ErrorMessage}");

        if (fastForwarded || ConfirmAsync is null)
        {
            return;
        }

        // A fast-forward was refused, almost always because the branches
        // diverged. Merging can conflict, so it is offered rather than done.
        var merge = await ConfirmAsync(
            "Branches have diverged",
            $"{repo.Name} cannot be fast-forwarded. Merge the remote branch instead? This may produce conflicts you will need to resolve.");

        if (!merge)
        {
            return;
        }

        await RunGitAsync(
            $"Merging into {repo.Status.Branch}",
            () => _git.MergeUpstreamAsync(repo.Path),
            result => result.Success
                ? $"Merged into {repo.Status.Branch}"
                : $"Merge stopped with conflicts. {Conflicts.Count} file{(Conflicts.Count == 1 ? string.Empty : "s")} to resolve.");
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

    private async Task CloneAsync()
    {
        if (PromptAsync is null || PickFolder is null)
        {
            return;
        }

        var url = await PromptAsync("Clone repository", "Repository URL", string.Empty);
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        var name = GitService.RepositoryNameFromUrl(url);

        var parent = await PickFolder();
        if (parent is null)
        {
            return;
        }

        // Say where it is going before starting. A clone can run for minutes
        // and the user should not have to wait to find out it is landing
        // somewhere they did not intend.
        var destination = Path.Combine(parent, name);
        _announcer.Announce($"Cloning {name} into {destination}");

        IsBusy = true;
        var done = _announcer.Operation($"Cloning {name}");

        try
        {
            var (result, path) = await _git.CloneAsync(
                url,
                parent,
                // Git writes progress to stderr. Route it to the status line
                // rather than the speech queue: it updates many times a
                // second and announcing each one would bury everything else.
                progress => _announcer.SetStatus(progress));

            if (!result.Success || path is null)
            {
                done($"Clone failed: {result.ErrorMessage}");
                _announcer.Announce($"Clone failed: {result.ErrorMessage}", Urgency.Assertive);
                return;
            }

            await _store.AddAsync(path);
            var item = new RepositoryItem(path);
            Repositories.Add(item);
            item.Status = await _git.GetStatusAsync(path);

            done($"Cloned {name}");
            SelectedRepository = item;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task SwitchBranchAsync(BranchInfo? branch)
    {
        if (branch is null || SelectedRepository is not { } repo || branch.IsCurrent)
        {
            return;
        }

        await RunGitAsync(
            $"Switching to {branch.Name}",
            () => _git.SwitchBranchAsync(repo.Path, branch.Name),
            result => result.Success
                ? $"Switched to {branch.Name}"
                // The usual failure is uncommitted work that would be
                // overwritten. Git says so at length; say it briefly.
                : result.ErrorMessage.Contains("would be overwritten", StringComparison.OrdinalIgnoreCase)
                    ? $"Cannot switch to {branch.Name}: you have uncommitted changes that would be overwritten. Commit or stash them first."
                    : $"Could not switch to {branch.Name}: {result.ErrorMessage}");
    }

    private async Task CreateBranchAsync()
    {
        if (PromptAsync is null || SelectedRepository is not { } repo)
        {
            return;
        }

        var name = await PromptAsync("New branch", "Branch name", string.Empty);
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var trimmed = name.Trim();

        await RunGitAsync(
            $"Creating branch {trimmed}",
            () => _git.CreateBranchAsync(repo.Path, trimmed),
            result => result.Success
                ? $"Created and switched to {trimmed}"
                : $"Could not create {trimmed}: {result.ErrorMessage}");
    }

    private async Task AbortMergeAsync()
    {
        if (SelectedRepository is not { } repo || ConfirmAsync is null)
        {
            return;
        }

        var ok = await ConfirmAsync(
            "Abort merge",
            "This throws away the merge and any conflict resolution you have done. Your commits are not affected.");

        if (!ok)
        {
            return;
        }

        await RunGitAsync(
            "Aborting merge",
            () => _git.AbortMergeAsync(repo.Path),
            result => result.Success ? "Merge aborted" : $"Could not abort: {result.ErrorMessage}");
    }

    private async Task ResolveAsync(FileChange? change, ConflictSide side)
    {
        if (change is null || SelectedRepository is not { } repo)
        {
            return;
        }

        var which = side == ConflictSide.Ours ? _oursLabel : _theirsLabel;

        await RunGitAsync(
            $"Resolving {change.FileName} using {which}",
            () => _git.ResolveUsingAsync(repo.Path, change.Path, side),
            result => result.Success
                ? $"Resolved {change.FileName} using {which}. {RemainingConflicts()}"
                : $"Could not resolve {change.FileName}: {result.ErrorMessage}");
    }

    private async Task MarkResolvedAsync(FileChange? change)
    {
        if (change is null || SelectedRepository is not { } repo)
        {
            return;
        }

        await RunGitAsync(
            $"Marking {change.FileName} resolved",
            () => _git.MarkResolvedAsync(repo.Path, change.Path),
            result => result.Success
                ? $"Marked {change.FileName} resolved. {RemainingConflicts()}"
                : $"Could not mark {change.FileName} resolved: {result.ErrorMessage}");
    }

    /// <summary>
    /// Counted after the refresh that follows each resolution, so the user
    /// hears how much is left without having to go and look.
    /// </summary>
    private string RemainingConflicts()
    {
        var left = Conflicts.Count;
        return left switch
        {
            0 => "All conflicts resolved. You can commit the merge now.",
            1 => "1 conflict left.",
            _ => $"{left} conflicts left.",
        };
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
