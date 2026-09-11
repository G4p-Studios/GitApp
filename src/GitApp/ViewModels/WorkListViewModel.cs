using System.Collections.ObjectModel;
using System.Windows.Input;
using GitApp.Accessibility;
using GitApp.Domain;
using GitApp.GitHub;

namespace GitApp.ViewModels;

/// <summary>
/// The issues list, or the pull requests list. Same screen, different kind.
/// </summary>
public sealed class WorkListViewModel : ObservableObject
{
    private readonly GitHubSession _session;
    private readonly GitHubRepository _listed;
    private readonly Announcer _announcer;

    private string _filter = string.Empty;
    private string _listSummary = "Loading";
    private bool _isBusy;
    private GitHubWorkItem? _selected;
    private WorkStateFilter? _stateFilter;
    private IReadOnlyList<GitHubWorkItem> _all = Array.Empty<GitHubWorkItem>();
    private int _total;
    private bool _truncated;

    public WorkListViewModel(
        GitHubSession session,
        GitHubRepository listed,
        GitHubWorkKind kind,
        Announcer? announcer = null)
    {
        _session = session;
        _listed = listed;
        Kind = kind;
        _announcer = announcer ?? Announcer.Current;

        Heading = kind == GitHubWorkKind.PullRequest ? "Pull requests" : "Issues";
        StateOptions = kind == GitHubWorkKind.PullRequest
            ? new[]
            {
                new WorkStateFilter("Open", GitHubItemState.Open),
                new WorkStateFilter("Closed", GitHubItemState.Closed),
                new WorkStateFilter("Merged", GitHubItemState.Merged),
                new WorkStateFilter("All", null),
            }
            : new[]
            {
                new WorkStateFilter("Open", GitHubItemState.Open),
                new WorkStateFilter("Closed", GitHubItemState.Closed),
                new WorkStateFilter("All", null),
            };

        _stateFilter = StateOptions[0];

        RefreshCommand = new AsyncCommand(LoadAsync, () => !IsBusy);
        OpenCommand = new AsyncCommand<GitHubWorkItem>(OpenAsync);
    }

    public GitHubWorkKind Kind { get; }

    public string Heading { get; }

    public IReadOnlyList<WorkStateFilter> StateOptions { get; }

    public ObservableCollection<GitHubWorkItem> Items { get; } = new();

    public ICommand RefreshCommand { get; }

    public ICommand OpenCommand { get; }

    public event EventHandler<GitHubWorkItem>? OpenRequested;

    public string RepositoryName => _listed.FullName;

    public WorkStateFilter? SelectedState
    {
        get => _stateFilter;
        set
        {
            if (!Set(ref _stateFilter, value) || value is null || IsBusy)
            {
                return;
            }

            _ = LoadAsync();
        }
    }

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
                (RefreshCommand as AsyncCommand)?.RaiseCanExecuteChanged();
            }
        }
    }

    public GitHubWorkItem? SelectedItem
    {
        get => _selected;
        set => Set(ref _selected, value);
    }

    public Task InitialiseAsync() => LoadAsync();

    public bool TryOpenSelected()
    {
        if (SelectedItem is null)
        {
            return false;
        }

        OpenRequested?.Invoke(this, SelectedItem);
        return true;
    }

    private Task OpenAsync(GitHubWorkItem? item)
    {
        if (item is not null)
        {
            OpenRequested?.Invoke(this, item);
        }

        return Task.CompletedTask;
    }

    private async Task LoadAsync()
    {
        if (_session.CreateClient() is not { } client)
        {
            _announcer.Announce("Sign in to GitHub first.", Urgency.Assertive);
            return;
        }

        IsBusy = true;
        var stateName = (_stateFilter?.Name ?? "Open").ToLowerInvariant();
        var done = _announcer.Operation($"Loading {stateName} {KindWordPlural} in {_listed.Name}");

        try
        {
            var result = Kind == GitHubWorkKind.PullRequest
                ? await client.GetPullRequestsAsync(_listed.Owner, _listed.Name, _stateFilter?.State)
                : await client.GetIssuesAsync(_listed.Owner, _listed.Name, _stateFilter?.State);

            if (!result.Success)
            {
                ListSummary = result.Error!;
                done(result.Error!);
                return;
            }

            _all = result.Value!.Items;
            _total = result.Value.TotalCount;
            _truncated = result.Value.Truncated;
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
            : _all.Where(Matches).ToList();

        Items.Clear();
        foreach (var item in matches)
        {
            Items.Add(item);
        }

        ListSummary = Describe(matches.Count, needle);

        if (announce)
        {
            _announcer.Announce(ListSummary);
        }
    }

    private bool Matches(GitHubWorkItem item)
    {
        var needle = _filter.Trim();
        return item.Title.Contains(needle, StringComparison.OrdinalIgnoreCase)
            || item.Number.ToString().Contains(needle, StringComparison.OrdinalIgnoreCase)
            || (item.Author?.Contains(needle, StringComparison.OrdinalIgnoreCase) ?? false)
            || item.Labels.Any(l => l.Contains(needle, StringComparison.OrdinalIgnoreCase))
            || (item.HeadRef?.Contains(needle, StringComparison.OrdinalIgnoreCase) ?? false)
            || (item.BaseRef?.Contains(needle, StringComparison.OrdinalIgnoreCase) ?? false);
    }

    private string Describe(int count, string needle)
    {
        var state = (_stateFilter?.Name ?? "Open").ToLowerInvariant();
        var noun = count == 1 ? KindWord : KindWordPlural;

        if (!string.IsNullOrEmpty(needle))
        {
            return count == 0
                ? $"No {KindWordPlural} match {needle}"
                : $"{count} {noun} matching {needle}";
        }

        if (_truncated)
        {
            return $"Showing {count} of {_total} {state} {KindWordPlural}. More exist; type to filter.";
        }

        return count == 0
            ? $"No {state} {KindWordPlural}"
            : $"{count} {state} {noun}";
    }

    private string KindWord => Kind == GitHubWorkKind.PullRequest ? "pull request" : "issue";

    private string KindWordPlural => Kind == GitHubWorkKind.PullRequest ? "pull requests" : "issues";
}
