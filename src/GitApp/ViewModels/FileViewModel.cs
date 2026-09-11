using GitApp.Accessibility;
using GitApp.Domain;
using GitApp.GitHub;

namespace GitApp.ViewModels;

/// <summary>
/// One remote file: the contents, and the sidebar facts.
/// </summary>
public sealed class FileViewModel : ObservableObject
{
    private readonly GitHubSession _session;
    private readonly GitHubRepository _listed;
    private readonly GitHubTreeEntry _entry;
    private readonly string _branch;
    private readonly Announcer _announcer;

    private string _title;
    private string _summary = "Loading";
    private string _unavailableMessage = string.Empty;
    private bool _showDocument;
    private bool _showLines;
    private bool _showUnavailable;
    private bool _isBusy;
    private GitHubFile? _file;
    private IReadOnlyList<FileLine> _lines = Array.Empty<FileLine>();
    private IReadOnlyList<ReadmeBlock> _bodyBlocks = Array.Empty<ReadmeBlock>();
    private FileLine? _selectedLine;

    public FileViewModel(
        GitHubSession session,
        GitHubRepository listed,
        GitHubTreeEntry entry,
        string branch,
        Announcer? announcer = null)
    {
        _session = session;
        _listed = listed;
        _entry = entry;
        _branch = string.IsNullOrWhiteSpace(branch) ? "HEAD" : branch;
        _announcer = announcer ?? Announcer.Current;
        _title = entry.Name;
        OpenOnGitHubCommand = new AsyncCommand(OpenOnGitHubAsync);
    }

    public System.Windows.Input.ICommand OpenOnGitHubCommand { get; }

    public event EventHandler? ContentsChanged;

    public string Title
    {
        get => _title;
        private set => Set(ref _title, value);
    }

    public string Summary
    {
        get => _summary;
        private set => Set(ref _summary, value);
    }

    public string UnavailableMessage
    {
        get => _unavailableMessage;
        private set => Set(ref _unavailableMessage, value);
    }

    public bool ShowDocument
    {
        get => _showDocument;
        private set => Set(ref _showDocument, value);
    }

    public bool ShowLines
    {
        get => _showLines;
        private set => Set(ref _showLines, value);
    }

    public bool ShowUnavailable
    {
        get => _showUnavailable;
        private set => Set(ref _showUnavailable, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set => Set(ref _isBusy, value);
    }

    public GitHubFile? File => _file;

    public IReadOnlyList<FileLine> Lines => _lines;

    public IReadOnlyList<ReadmeBlock> BodyBlocks => _bodyBlocks;

    public IReadOnlyList<string> AboutFacts =>
        _file?.AboutFacts ?? Array.Empty<string>();

    public string DocumentBaseUri
    {
        get
        {
            var path = _entry.Path.Replace('\\', '/').Trim('/');
            var slash = path.LastIndexOf('/');
            var dir = slash < 0 ? string.Empty : path[..slash];
            var root = _listed.HtmlUrl.TrimEnd('/');
            return string.IsNullOrEmpty(dir)
                ? $"{root}/blob/{_branch}/"
                : $"{root}/blob/{_branch}/{dir}/";
        }
    }

    public FileLine? SelectedLine
    {
        get => _selectedLine;
        set => Set(ref _selectedLine, value);
    }

    public async Task InitialiseAsync()
    {
        if (_session.CreateClient() is not { } client)
        {
            _announcer.Announce("Sign in to GitHub first.", Urgency.Assertive);
            return;
        }

        IsBusy = true;
        var done = _announcer.Operation($"Opening {_entry.Name}");

        try
        {
            var result = await client.GetFileAsync(
                _listed.Owner,
                _listed.Name,
                _branch,
                _entry.Path,
                _listed.HtmlUrl);

            if (!result.Success)
            {
                Summary = result.Error!;
                UnavailableMessage = result.Error!;
                ShowUnavailable = true;
                ShowDocument = false;
                ShowLines = false;
                done(result.Error!);
                ContentsChanged?.Invoke(this, EventArgs.Empty);
                return;
            }

            _file = result.Value! with
            {
                LastSubject = _entry.LastSubject,
                LastTouched = _entry.LastTouched,
            };

            Title = _file.Name;
            Summary = _file.Summary;
            UnavailableMessage = _file.UnavailableMessage;
            ShowDocument = _file.ShowDocument;
            ShowLines = _file.ShowLines;
            ShowUnavailable = _file.ShowUnavailable;
            _lines = _file.Lines;
            SelectedLine = _lines.FirstOrDefault();
            _bodyBlocks = _file.ShowDocument && !string.IsNullOrEmpty(_file.Text)
                ? ReadmeDocument.Parse(_file.Text)
                : Array.Empty<ReadmeBlock>();

            Raise(nameof(File));
            Raise(nameof(Lines));
            Raise(nameof(BodyBlocks));
            Raise(nameof(AboutFacts));
            ContentsChanged?.Invoke(this, EventArgs.Empty);
            done(_file.Summary);
        }
        finally
        {
            client.Dispose();
            _announcer.ClearStatus();
            IsBusy = false;
        }
    }

    private async Task OpenOnGitHubAsync()
    {
        var url = _file?.HtmlUrl ?? GitHubFile.BlobUrl(_listed.HtmlUrl, _branch, _entry.Path);
        if (string.IsNullOrEmpty(url))
        {
            return;
        }

        try
        {
            await Launcher.Default.OpenAsync(url);
            _announcer.Announce($"Opened {_entry.Name} in your browser");
        }
        catch (Exception)
        {
            _announcer.Announce("Could not open your browser.");
        }
    }
}
