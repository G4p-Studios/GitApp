using GitApp.Accessibility;
using GitApp.Domain;
using GitApp.GitHub;

namespace GitApp.ViewModels;

/// <summary>
/// One issue or pull request: the conversation and the sidebar facts.
/// </summary>
public sealed class WorkDetailViewModel : ObservableObject
{
    private readonly GitHubSession _session;
    private readonly GitHubRepository _listed;
    private readonly GitHubWorkItem _listedItem;
    private readonly Announcer _announcer;

    private string _title;
    private string _metadata = string.Empty;
    private string _commentsHeading = "Comments";
    private bool _isBusy;
    private GitHubWorkDetail? _detail;
    private IReadOnlyList<ReadmeBlock> _bodyBlocks = Array.Empty<ReadmeBlock>();

    public WorkDetailViewModel(
        GitHubSession session,
        GitHubRepository listed,
        GitHubWorkItem item,
        Announcer? announcer = null)
    {
        _session = session;
        _listed = listed;
        _listedItem = item;
        _announcer = announcer ?? Announcer.Current;
        _title = item.Title;
        _metadata = item.AccessibleName;
        OpenOnGitHubCommand = new AsyncCommand(OpenOnGitHubAsync);
    }

    public System.Windows.Input.ICommand OpenOnGitHubCommand { get; }

    public event EventHandler? ConversationChanged;

    public GitHubWorkKind Kind => _listedItem.Kind;

    public string KindWord => _listedItem.KindWord;

    public string Title
    {
        get => _title;
        private set => Set(ref _title, value);
    }

    public string Metadata
    {
        get => _metadata;
        private set => Set(ref _metadata, value);
    }

    public string CommentsHeading
    {
        get => _commentsHeading;
        private set => Set(ref _commentsHeading, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set => Set(ref _isBusy, value);
    }

    public GitHubWorkDetail? Detail => _detail;

    public IReadOnlyList<ReadmeBlock> BodyBlocks => _bodyBlocks;

    public IReadOnlyList<string> AboutFacts =>
        _detail?.AboutFacts ?? Array.Empty<string>();

    public string DocumentBaseUri => _listed.HtmlUrl.TrimEnd('/') + "/";

    public async Task InitialiseAsync()
    {
        if (_session.CreateClient() is not { } client)
        {
            _announcer.Announce("Sign in to GitHub first.", Urgency.Assertive);
            return;
        }

        IsBusy = true;
        var done = _announcer.Operation($"Loading {_listedItem.KindWord} {_listedItem.Number}");

        try
        {
            var result = _listedItem.Kind == GitHubWorkKind.PullRequest
                ? await client.GetPullRequestAsync(_listed.Owner, _listed.Name, _listedItem.Number)
                : await client.GetIssueAsync(_listed.Owner, _listed.Name, _listedItem.Number);

            if (!result.Success)
            {
                Metadata = result.Error!;
                done(result.Error!);
                return;
            }

            _detail = result.Value;
            Title = _detail!.Title;
            Metadata = _detail.Metadata;
            CommentsHeading = _detail.CommentsHeading;
            _bodyBlocks = string.IsNullOrWhiteSpace(_detail.BodyMarkdown)
                ? Array.Empty<ReadmeBlock>()
                : ReadmeDocument.Parse(_detail.BodyMarkdown);

            Raise(nameof(Detail));
            Raise(nameof(BodyBlocks));
            Raise(nameof(AboutFacts));
            ConversationChanged?.Invoke(this, EventArgs.Empty);
            done($"{_listedItem.KindWord} {_listedItem.Number}, {_detail.CommentsHeading}");
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
        var url = _detail?.Item.HtmlUrl ?? _listedItem.HtmlUrl;
        if (string.IsNullOrEmpty(url))
        {
            return;
        }

        try
        {
            await Launcher.Default.OpenAsync(url);
            _announcer.Announce($"Opened {_listedItem.KindWord} {_listedItem.Number} in your browser");
        }
        catch (Exception)
        {
            _announcer.Announce("Could not open your browser.");
        }
    }
}
