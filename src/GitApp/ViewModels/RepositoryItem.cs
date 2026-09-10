using GitApp.Domain;

namespace GitApp.ViewModels;

/// <summary>
/// One repository in the sidebar.
/// </summary>
public sealed class RepositoryItem : ObservableObject
{
    private RepoStatus _status = RepoStatus.Empty;
    private bool _isBusy;

    public RepositoryItem(string path)
    {
        Path = path;
    }

    public string Path { get; }

    public string Name => System.IO.Path.GetFileName(Path.TrimEnd(
        System.IO.Path.DirectorySeparatorChar,
        System.IO.Path.AltDirectorySeparatorChar));

    public RepoStatus Status
    {
        get => _status;
        set
        {
            if (Set(ref _status, value))
            {
                Raise(nameof(Detail));
                Raise(nameof(AccessibleName));
            }
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (Set(ref _isBusy, value))
            {
                Raise(nameof(AccessibleName));
            }
        }
    }

    public string Detail => $"{Status.Branch} · {Status.SyncDescription}";

    /// <summary>
    /// The whole row as one string, in reading order. See
    /// docs/ARCHITECTURE.md 3.3.
    /// </summary>
    public string AccessibleName
    {
        get
        {
            var name = $"{Name}, {Status.Branch}, {Status.SyncDescription}";

            if (Status.HasChanges)
            {
                name += $", {Status.ChangeSummary}";
            }

            // Busy last: it is transient, and putting it first would make
            // every repository sound the same while a fetch is running.
            return _isBusy ? $"{name}, busy" : name;
        }
    }
}
