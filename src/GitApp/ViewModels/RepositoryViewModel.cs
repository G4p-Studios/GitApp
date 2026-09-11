using System.Collections.ObjectModel;
using System.Windows.Input;
using GitApp.Accessibility;
using GitApp.Domain;
using GitApp.GitHub;
using GitApp.Services;

namespace GitApp.ViewModels;

/// <summary>
/// The github.com-style repository screen: files, readme, about.
/// </summary>
public sealed class RepositoryViewModel : ObservableObject
{
    private readonly GitHubSession _session;
    private readonly Announcer _announcer;
    private readonly GitService _git;
    private readonly GitHubRepository _listed;

    private string _path = string.Empty;
    private string _title = string.Empty;
    private string _pathDescription = string.Empty;
    private string _entrySummary = "Loading";
    private string _latestSummary = string.Empty;
    private string _latestSubject = string.Empty;
    private string _latestDetail = string.Empty;
    private string _readmeHeading = "Readme";
    private string _readmeEmpty = "This folder has no readme";
    private bool _hasPath;
    private bool _hasReadme;
    private bool _isBusy;
    private bool _ignoreBranch;
    private string? _loadedBranch;
    private GitHubBranch? _selectedBranch;
    private GitHubTreeEntry? _selectedEntry;
    private string? _htmlUrl;
    private IReadOnlyList<ReadmeBlock> _readmeBlocks = Array.Empty<ReadmeBlock>();

    public RepositoryViewModel(
        GitHubSession session,
        GitHubRepository listed,
        Announcer? announcer = null,
        GitService? git = null)
    {
        _session = session;
        _listed = listed;
        _announcer = announcer ?? Announcer.Current;
        _git = git ?? new GitService();

        Title = listed.FullName;
        PathDescription = listed.FullName;
        _htmlUrl = listed.HtmlUrl;

        CloneCommand = new AsyncCommand(CloneAsync, () => !IsBusy);
        OpenOnGitHubCommand = new AsyncCommand(OpenOnGitHubAsync);
        OpenEntryCommand = new AsyncCommand<GitHubTreeEntry>(e =>
        {
            OpenEntry(e);
            return Task.CompletedTask;
        });
    }

    public ObservableCollection<GitHubTreeEntry> Entries { get; } = new();

    public ObservableCollection<GitHubBranch> Branches { get; } = new();

    public ObservableCollection<string> AboutFacts { get; } = new();

    /// <summary>The one About fact that opens a screen. See GitHubRepoView.ReleasesFact.</summary>
    public string? ReleasesFact { get; private set; }

    public ICommand CloneCommand { get; }

    public ICommand OpenOnGitHubCommand { get; }

    public ICommand OpenEntryCommand { get; }

    public Func<Task<string?>>? PickFolder { get; set; }

    public event EventHandler<string>? Cloned;

    public event EventHandler? ReadmeChanged;

    public event EventHandler<GitHubTreeEntry>? OpenFileRequested;

    public string Title
    {
        get => _title;
        private set => Set(ref _title, value);
    }

    public string PathDescription
    {
        get => _pathDescription;
        private set => Set(ref _pathDescription, value);
    }

    public string EntrySummary
    {
        get => _entrySummary;
        private set => Set(ref _entrySummary, value);
    }

    public string LatestSummary
    {
        get => _latestSummary;
        private set
        {
            if (Set(ref _latestSummary, value))
            {
                Raise(nameof(HasLatest));
            }
        }
    }

    public bool HasLatest => !string.IsNullOrEmpty(_latestSummary);

    /// <summary>The commit subject, shown on its own line as github.com does.</summary>
    public string LatestSubject
    {
        get => _latestSubject;
        private set => Set(ref _latestSubject, value);
    }

    /// <summary>Author, age and hash, the second line under the subject.</summary>
    public string LatestDetail
    {
        get => _latestDetail;
        private set => Set(ref _latestDetail, value);
    }

    public bool HasPath
    {
        get => _hasPath;
        private set => Set(ref _hasPath, value);
    }

    public string ReadmeHeading
    {
        get => _readmeHeading;
        private set => Set(ref _readmeHeading, value);
    }

    public string ReadmeEmpty
    {
        get => _readmeEmpty;
        private set => Set(ref _readmeEmpty, value);
    }

    public bool HasReadme
    {
        get => _hasReadme;
        private set
        {
            if (Set(ref _hasReadme, value))
            {
                Raise(nameof(NoReadme));
            }
        }
    }

    public bool NoReadme => !_hasReadme;

    public IReadOnlyList<ReadmeBlock> ReadmeBlocks => _readmeBlocks;

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (Set(ref _isBusy, value))
            {
                (CloneCommand as AsyncCommand)?.RaiseCanExecuteChanged();
            }
        }
    }

    public string CurrentBranch =>
        _selectedBranch?.Name ?? _loadedBranch ?? "HEAD";

    public GitHubTreeEntry? SelectedEntry
    {
        get => _selectedEntry;
        set => Set(ref _selectedEntry, value);
    }

    public GitHubBranch? SelectedBranch
    {
        get => _selectedBranch;
        set
        {
            if (!Set(ref _selectedBranch, value))
            {
                return;
            }

            if (_ignoreBranch || value is null || IsBusy || value.Name == _loadedBranch)
            {
                return;
            }

            _path = string.Empty;
            _ = LoadAsync($"Loading {value.Name}");
        }
    }

    public Task InitialiseAsync() => LoadAsync($"Loading {_listed.FullName}");

    /// <summary>
    /// Left, or Escape inside a directory: one folder up.
    /// </summary>
    public bool TryGoUp()
    {
        if (string.IsNullOrEmpty(_path) || IsBusy)
        {
            return false;
        }

        _path = ParentOf(_path);
        _ = LoadAsync("Opening parent folder");
        return true;
    }

    private static string ParentOf(string path)
    {
        var slash = path.Replace('\\', '/').Trim('/').LastIndexOf('/');
        return slash < 0 ? string.Empty : path[..slash];
    }

    /// <summary>
    /// Enter, Space, or a click: open the selected row.
    /// </summary>
    public bool TryOpenSelected() => OpenEntry(SelectedEntry);

    private bool OpenEntry(GitHubTreeEntry? entry)
    {
        if (entry is null || IsBusy)
        {
            return false;
        }

        if (entry.Kind == GitHubEntryKind.Parent)
        {
            return TryGoUp();
        }

        if (entry.Kind == GitHubEntryKind.Folder)
        {
            _path = entry.Path;
            _ = LoadAsync($"Opening {entry.Name}");
            return true;
        }

        if (entry.Kind == GitHubEntryKind.Submodule)
        {
            _announcer.Announce($"{entry.Name} is a submodule, not a file.");
            return true;
        }

        OpenFileRequested?.Invoke(this, entry);
        return true;
    }

    private async Task LoadAsync(string startMessage)
    {
        if (_session.CreateClient() is not { } client)
        {
            _announcer.Announce("Sign in to GitHub first.", Urgency.Assertive);
            return;
        }

        IsBusy = true;
        var done = _announcer.Operation(startMessage);

        try
        {
            var result = await client.GetRepositoryViewAsync(
                _listed.Owner,
                _listed.Name,
                _selectedBranch?.Name,
                _path);

            if (!result.Success)
            {
                EntrySummary = result.Error!;
                done(result.Error!);
                return;
            }

            Apply(result.Value!);
            done(EntrySummary);
        }
        finally
        {
            client.Dispose();
            _announcer.ClearStatus();
            IsBusy = false;
        }
    }

    private void Apply(GitHubRepoView view)
    {
        _path = view.Path;
        _loadedBranch = view.CurrentBranch;
        Title = view.Title;
        PathDescription = view.PathDescription;
        EntrySummary = view.EntrySummary;
        HasPath = !string.IsNullOrEmpty(view.Path);
        LatestSummary = view.Latest?.AccessibleName ?? string.Empty;
        LatestSubject = view.Latest?.Subject ?? string.Empty;
        LatestDetail = view.Latest?.Detail ?? string.Empty;
        _htmlUrl = string.IsNullOrEmpty(view.HtmlUrl) ? _listed.HtmlUrl : view.HtmlUrl;

        _ignoreBranch = true;
        try
        {
            Branches.Clear();
            foreach (var branch in view.Branches)
            {
                Branches.Add(branch);
            }

            _selectedBranch = Branches.FirstOrDefault(b => b.Name == view.CurrentBranch)
                ?? Branches.FirstOrDefault();
            Raise(nameof(SelectedBranch));
        }
        finally
        {
            _ignoreBranch = false;
        }

        Entries.Clear();
        foreach (var entry in view.Entries)
        {
            Entries.Add(entry);
        }

        SelectedEntry = Entries.FirstOrDefault();

        ReleasesFact = view.ReleasesFact;
        AboutFacts.Clear();
        foreach (var fact in view.AboutFacts)
        {
            AboutFacts.Add(fact);
        }

        if (!string.IsNullOrEmpty(view.ReadmeMarkdown))
        {
            ReadmeHeading = view.ReadmeName ?? "Readme";
            ReadmeEmpty = string.Empty;
            HasReadme = true;
            _readmeBlocks = ReadmeDocument.Parse(view.ReadmeMarkdown);
        }
        else
        {
            ReadmeHeading = "Readme";
            ReadmeEmpty = view.ReadmeName is null
                ? "This folder has no readme"
                : "The readme could not be shown because it is not readable text.";
            HasReadme = false;
            _readmeBlocks = Array.Empty<ReadmeBlock>();
        }

        Raise(nameof(ReadmeBlocks));
        ReadmeChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task CloneAsync()
    {
        if (PickFolder is null)
        {
            return;
        }

        var parent = await PickFolder();
        if (string.IsNullOrEmpty(parent))
        {
            _announcer.Announce("Clone cancelled");
            return;
        }

        var done = _announcer.Operation($"Cloning {_listed.FullName}");

        var (result, path) = await _git.CloneAsync(
            _listed.CloneUrl,
            parent,
            progress => _announcer.SetStatus(progress));

        _announcer.ClearStatus();

        if (!result.Success)
        {
            done($"Clone of {_listed.Name} failed. {result.ErrorMessage}");
            return;
        }

        done($"Cloned {_listed.Name} into {path}");
        Cloned?.Invoke(this, path!);
    }

    private async Task OpenOnGitHubAsync()
    {
        if (string.IsNullOrEmpty(_htmlUrl))
        {
            return;
        }

        try
        {
            await Launcher.Default.OpenAsync(_htmlUrl);
            _announcer.Announce($"Opened {_listed.FullName} in your browser");
        }
        catch (Exception)
        {
            _announcer.Announce("Could not open your browser.");
        }
    }
}
