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
    private string _draft = string.Empty;
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
        PostCommentCommand = new AsyncCommand(PostCommentAsync, () => CanPost);
        ToggleStateCommand = new AsyncCommand(ToggleStateAsync, () => !_isBusy && CanChangeState);
        MergeCommand = new AsyncCommand(MergeAsync, () => !_isBusy && CanMerge);
        ApproveCommand = new AsyncCommand(() => ReviewAsync(ReviewEvent.Approve), () => !_isBusy && IsOpenPullRequest);
        RequestChangesCommand = new AsyncCommand(() => ReviewAsync(ReviewEvent.RequestChanges), () => !_isBusy && IsOpenPullRequest);
    }

    public System.Windows.Input.ICommand OpenOnGitHubCommand { get; }

    public AsyncCommand PostCommentCommand { get; }

    public AsyncCommand ToggleStateCommand { get; }

    public AsyncCommand MergeCommand { get; }

    public AsyncCommand ApproveCommand { get; }

    public AsyncCommand RequestChangesCommand { get; }

    /// <summary>
    /// Supplied by the page: show the allowed methods and return the one
    /// chosen, or null for cancel. The choice is the confirmation; a merge
    /// is not undone with one keystroke the way a close is.
    /// </summary>
    public Func<IReadOnlyList<MergeMethod>, Task<MergeMethod?>>? ChooseMergeMethodAsync { get; set; }

    public event EventHandler? ConversationChanged;

    /// <summary>Raised after a comment or review is posted, with the entry to append.</summary>
    public event EventHandler<GitHubComment>? CommentPosted;

    /// <summary>State changed or merged: the sidebar facts and the toolbar buttons are stale.</summary>
    public event EventHandler? FactsChanged;

    /// <summary>The comment being written. Never cleared on failure.</summary>
    public string Draft
    {
        get => _draft;
        set
        {
            if (Set(ref _draft, value))
            {
                Raise(nameof(HasDraft));
                PostCommentCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool HasDraft => !string.IsNullOrWhiteSpace(_draft);

    public bool CanPost => HasDraft && !_isBusy && _detail is { CanComment: true };

    public bool CanChangeState => _detail is { CanChangeState: true };

    public bool CanMerge => _detail is { CanMerge: true };

    public bool IsOpenPullRequest => _detail is { IsOpenPullRequest: true, CanComment: true };

    public string StateActionLabel => _detail?.StateActionLabel ?? $"Close {_listedItem.KindWord}";

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
        private set
        {
            if (Set(ref _isBusy, value))
            {
                RaiseCommands();
            }
        }
    }

    private void RaiseCommands()
    {
        PostCommentCommand.RaiseCanExecuteChanged();
        ToggleStateCommand.RaiseCanExecuteChanged();
        MergeCommand.RaiseCanExecuteChanged();
        ApproveCommand.RaiseCanExecuteChanged();
        RequestChangesCommand.RaiseCanExecuteChanged();
    }

    /// <summary>Everything derived from the detail record, after it changes.</summary>
    private void RaiseDetail()
    {
        Raise(nameof(Detail));
        Raise(nameof(AboutFacts));
        Raise(nameof(CanChangeState));
        Raise(nameof(CanMerge));
        Raise(nameof(IsOpenPullRequest));
        Raise(nameof(StateActionLabel));
        RaiseCommands();
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

            Raise(nameof(BodyBlocks));
            RaiseDetail();
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

    /// <summary>
    /// Post the draft. The draft survives a failure, because the one
    /// thing worse than a comment that did not post is a comment that
    /// did not post and is gone.
    /// </summary>
    public async Task PostCommentAsync()
    {
        if (!HasDraft)
        {
            _announcer.Announce("Write a comment first.");
            return;
        }

        if (_detail is not { CanComment: true } detail)
        {
            _announcer.Announce("The conversation has not loaded yet.");
            return;
        }

        if (_session.CreateClient() is not { } client)
        {
            _announcer.Announce("Sign in to GitHub first.", Urgency.Assertive);
            return;
        }

        IsBusy = true;
        var done = _announcer.Operation("Posting comment");

        try
        {
            var result = await client.AddCommentAsync(detail.NodeId!, _draft.Trim());

            if (!result.Success)
            {
                done(result.Error!);
                return;
            }

            _detail = detail.WithComment(result.Value!);
            CommentsHeading = _detail.CommentsHeading;
            Draft = string.Empty;

            RaiseDetail();
            CommentPosted?.Invoke(this, result.Value!);
            done($"Comment posted. {_detail.CommentsHeading}.");
        }
        finally
        {
            client.Dispose();
            IsBusy = false;
        }
    }

    /// <summary>
    /// Close or reopen. No confirmation: github.com has none, and the
    /// opposite action is one press of the same button.
    /// </summary>
    public async Task ToggleStateAsync()
    {
        if (_detail is not { CanChangeState: true } detail)
        {
            return;
        }

        if (_session.CreateClient() is not { } client)
        {
            _announcer.Announce("Sign in to GitHub first.", Urgency.Assertive);
            return;
        }

        var opening = detail.Item.State != GitHubItemState.Open;
        var what = $"{_listedItem.KindWord} {_listedItem.Number}";

        IsBusy = true;
        var done = _announcer.Operation(opening ? $"Reopening {what}" : $"Closing {what}");

        try
        {
            var result = await client.SetStateAsync(_listedItem.Kind, detail.NodeId!, opening);

            if (!result.Success)
            {
                done(result.Error!);
                return;
            }

            ApplyState(result.Value);
            done(result.Value == GitHubItemState.Open
                ? $"{Capitalise(what)} reopened."
                : $"{Capitalise(what)} closed.");
        }
        finally
        {
            client.Dispose();
            IsBusy = false;
        }
    }

    /// <summary>Merge, after the page has had the user pick a method.</summary>
    public async Task MergeAsync()
    {
        if (_detail is not { CanMerge: true } detail || ChooseMergeMethodAsync is null)
        {
            return;
        }

        if (await ChooseMergeMethodAsync(detail.MergeMethods!) is not { } method)
        {
            _announcer.Announce("Merge cancelled.");
            return;
        }

        if (_session.CreateClient() is not { } client)
        {
            _announcer.Announce("Sign in to GitHub first.", Urgency.Assertive);
            return;
        }

        var what = $"pull request {_listedItem.Number}";

        IsBusy = true;
        var done = _announcer.Operation($"Merging {what} into {detail.Item.BaseRef}");

        try
        {
            var result = await client.MergePullRequestAsync(detail.NodeId!, method);

            if (!result.Success)
            {
                done(result.Error!);
                return;
            }

            ApplyState(GitHubItemState.Merged);
            done($"{Capitalise(what)} merged into {detail.Item.BaseRef}.");
        }
        finally
        {
            client.Dispose();
            IsBusy = false;
        }
    }

    /// <summary>
    /// Approve or request changes, with the draft as the review's words.
    /// Requesting changes with nothing said is refused before it reaches
    /// GitHub, which would refuse it too, less clearly.
    /// </summary>
    public async Task ReviewAsync(ReviewEvent verdict)
    {
        if (_detail is not { IsOpenPullRequest: true, CanComment: true } detail)
        {
            return;
        }

        if (verdict == ReviewEvent.RequestChanges && !HasDraft)
        {
            _announcer.Announce("Write what needs to change first, in the comment box.");
            return;
        }

        if (_session.CreateClient() is not { } client)
        {
            _announcer.Announce("Sign in to GitHub first.", Urgency.Assertive);
            return;
        }

        var what = $"pull request {_listedItem.Number}";

        IsBusy = true;
        var done = _announcer.Operation(verdict == ReviewEvent.Approve
            ? $"Approving {what}"
            : $"Requesting changes on {what}");

        try
        {
            var result = await client.ReviewPullRequestAsync(detail.NodeId!, verdict, _draft.Trim());

            if (!result.Success)
            {
                done(result.Error!);
                return;
            }

            _detail = detail.WithComment(result.Value!) with
            {
                ReviewDecision = verdict == ReviewEvent.Approve ? "APPROVED" : "CHANGES_REQUESTED",
            };
            CommentsHeading = _detail.CommentsHeading;
            Draft = string.Empty;

            RaiseDetail();
            CommentPosted?.Invoke(this, result.Value!);
            FactsChanged?.Invoke(this, EventArgs.Empty);
            done(verdict == ReviewEvent.Approve
                ? $"Approved {what}."
                : $"Requested changes on {what}.");
        }
        finally
        {
            client.Dispose();
            IsBusy = false;
        }
    }

    private void ApplyState(GitHubItemState state)
    {
        _detail = _detail!.WithState(state);
        Metadata = _detail.Metadata;
        RaiseDetail();
        FactsChanged?.Invoke(this, EventArgs.Empty);
    }

    private static string Capitalise(string text) =>
        char.ToUpperInvariant(text[0]) + text[1..];

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
