using System.Collections.ObjectModel;
using GitApp.Accessibility;
using GitApp.Domain;
using GitApp.GitHub;

namespace GitApp.ViewModels;

/// <summary>
/// The releases screen: the list, the selected release's notes and
/// assets, and the form for a new one. See docs/RELEASES.md.
/// </summary>
public sealed class ReleasesViewModel : ObservableObject
{
    private readonly GitHubSession _session;
    private readonly GitHubRepository _listed;
    private readonly Announcer _announcer;

    private string _listSummary = "Loading";
    private bool _isBusy;
    private GitHubRelease? _selected;
    private IReadOnlyList<ReadmeBlock> _notesBlocks = Array.Empty<ReadmeBlock>();

    private string _tag = string.Empty;
    private string? _target;
    private string _newTitle = string.Empty;
    private string _notes = string.Empty;
    private bool _isPrerelease;
    private bool _isDraft;

    public ReleasesViewModel(GitHubSession session, GitHubRepository listed, Announcer? announcer = null)
    {
        _session = session;
        _listed = listed;
        _announcer = announcer ?? Announcer.Current;

        RefreshCommand = new AsyncCommand(LoadAsync, () => !IsBusy);
        PublishCommand = new AsyncCommand(PublishAsync, () => CanPublish);
        OpenOnGitHubCommand = new AsyncCommand(OpenOnGitHubAsync, () => SelectedRelease is not null);
    }

    public AsyncCommand RefreshCommand { get; }

    public AsyncCommand PublishCommand { get; }

    public AsyncCommand OpenOnGitHubCommand { get; }

    public ObservableCollection<GitHubRelease> Items { get; } = new();

    public ObservableCollection<string> Branches { get; } = new();

    /// <summary>The list has been reloaded, or a release added to it.</summary>
    public event EventHandler? ListChanged;

    /// <summary>The selected release changed; the details pane is stale.</summary>
    public event EventHandler? SelectionChanged;

    public string RepositoryName => _listed.FullName;

    public string DocumentBaseUri => _listed.HtmlUrl.TrimEnd('/') + "/";

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
                RefreshCommand.RaiseCanExecuteChanged();
                PublishCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public GitHubRelease? SelectedRelease
    {
        get => _selected;
        set
        {
            if (!Set(ref _selected, value))
            {
                return;
            }

            _notesBlocks = string.IsNullOrWhiteSpace(value?.NotesMarkdown)
                ? Array.Empty<ReadmeBlock>()
                : ReadmeDocument.Parse(value!.NotesMarkdown!);

            Raise(nameof(HasSelection));
            Raise(nameof(DetailsTitle));
            Raise(nameof(DetailsMetadata));
            Raise(nameof(AssetsHeading));
            Raise(nameof(HasNotes));
            Raise(nameof(NoNotes));
            OpenOnGitHubCommand.RaiseCanExecuteChanged();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public bool HasSelection => _selected is not null;

    public string DetailsTitle => _selected?.Title ?? "No release selected";

    public string DetailsMetadata => _selected?.Metadata ?? string.Empty;

    public string AssetsHeading => _selected?.AssetsHeading ?? string.Empty;

    public IReadOnlyList<ReadmeBlock> NotesBlocks => _notesBlocks;

    public bool HasNotes => _notesBlocks.Count > 0;

    public bool NoNotes => _selected is not null && _notesBlocks.Count == 0;

    // The form.

    public string Tag
    {
        get => _tag;
        set
        {
            if (Set(ref _tag, value))
            {
                Raise(nameof(HasUnsavedForm));
                PublishCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string? Target
    {
        get => _target;
        set => Set(ref _target, value);
    }

    public string NewTitle
    {
        get => _newTitle;
        set
        {
            if (Set(ref _newTitle, value))
            {
                Raise(nameof(HasUnsavedForm));
            }
        }
    }

    public string Notes
    {
        get => _notes;
        set
        {
            if (Set(ref _notes, value))
            {
                Raise(nameof(HasUnsavedForm));
            }
        }
    }

    public bool IsPrerelease
    {
        get => _isPrerelease;
        set => Set(ref _isPrerelease, value);
    }

    public bool IsDraft
    {
        get => _isDraft;
        set
        {
            if (Set(ref _isDraft, value))
            {
                Raise(nameof(PublishLabel));
            }
        }
    }

    /// <summary>"Publish release", or "Save draft" once Draft is ticked, so the button says what it will do.</summary>
    public string PublishLabel => Form.ActionWord;

    public bool CanPublish => !IsBusy && !string.IsNullOrWhiteSpace(_tag);

    /// <summary>Anything typed into the form that Escape would throw away.</summary>
    public bool HasUnsavedForm =>
        !string.IsNullOrWhiteSpace(_tag)
        || !string.IsNullOrWhiteSpace(_newTitle)
        || !string.IsNullOrWhiteSpace(_notes);

    private NewRelease Form => new(_tag, _target, _newTitle, _notes, _isPrerelease, _isDraft);

    public Task InitialiseAsync() => LoadAsync();

    private async Task LoadAsync()
    {
        if (_session.CreateClient() is not { } client)
        {
            _announcer.Announce("Sign in to GitHub first.", Urgency.Assertive);
            return;
        }

        IsBusy = true;
        var done = _announcer.Operation($"Loading releases in {_listed.Name}");

        try
        {
            var result = await client.GetReleasesAsync(_listed.Owner, _listed.Name);

            if (!result.Success)
            {
                ListSummary = result.Error!;
                done(result.Error!);
                return;
            }

            var list = result.Value!;

            Items.Clear();
            foreach (var release in list.Items)
            {
                Items.Add(release);
            }

            Branches.Clear();
            foreach (var branch in list.Branches)
            {
                Branches.Add(branch);
            }

            Target ??= list.DefaultBranch ?? Branches.FirstOrDefault();
            ListSummary = list.Summary;
            SelectedRelease = Items.FirstOrDefault();
            ListChanged?.Invoke(this, EventArgs.Empty);
            done(ListSummary);
        }
        finally
        {
            client.Dispose();
            _announcer.ClearStatus();
            IsBusy = false;
        }
    }

    /// <summary>
    /// Create the release. The form survives a failure; the tag the user
    /// typed is the thing most worth not retyping.
    /// </summary>
    public async Task PublishAsync()
    {
        var form = Form;

        if (form.Problem is { } problem)
        {
            _announcer.Announce(problem, Urgency.Assertive);
            return;
        }

        if (_session.CreateClient() is not { } client)
        {
            _announcer.Announce("Sign in to GitHub first.", Urgency.Assertive);
            return;
        }

        var tag = form.TagName.Trim();

        IsBusy = true;
        var done = _announcer.Operation(form.IsDraft
            ? $"Saving draft {tag}"
            : $"Publishing {tag}");

        try
        {
            var result = await client.CreateReleaseAsync(_listed.Owner, _listed.Name, form);

            if (!result.Success)
            {
                done(result.Error!);
                return;
            }

            var created = result.Value!;
            Items.Insert(0, created);
            ListSummary = Items.Count == 1 ? "1 release" : $"{Items.Count} releases";

            Tag = string.Empty;
            NewTitle = string.Empty;
            Notes = string.Empty;
            IsPrerelease = false;
            IsDraft = false;

            SelectedRelease = created;
            ListChanged?.Invoke(this, EventArgs.Empty);
            done(created.IsDraft
                ? $"Draft {created.Title} saved. It is not public until published on github.com."
                : $"{created.Title} published.");
        }
        finally
        {
            client.Dispose();
            IsBusy = false;
        }
    }

    private async Task OpenOnGitHubAsync()
    {
        if (_selected is not { HtmlUrl: { Length: > 0 } url } release)
        {
            return;
        }

        try
        {
            await Launcher.Default.OpenAsync(url);
            _announcer.Announce($"Opened {release.Title} in your browser");
        }
        catch (Exception)
        {
            _announcer.Announce("Could not open your browser.");
        }
    }
}
