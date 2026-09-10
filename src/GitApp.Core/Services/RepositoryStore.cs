using System.Text.Json;

namespace GitApp.Services;

/// <summary>
/// The list of local repositories the user has added.
///
/// Plain JSON in the app data folder. Nothing here is a secret: paths only.
/// Credentials live in the platform credential store, never on disk in our
/// own files. See docs/ARCHITECTURE.md 4.5.
/// </summary>
public sealed class RepositoryStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private readonly string _path;
    private List<string> _paths = new();

    public RepositoryStore(string? filePath = null)
    {
        // Default location resolved without MAUI, so this library stays
        // free of any UI framework and can be unit tested and reused by a
        // different shell. See docs/ARCHITECTURE.md 4.1.
        _path = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "GitApp",
            "repositories.json");
    }

    public IReadOnlyList<string> Paths => _paths;

    /// <summary>
    /// Set when the saved list could not be read. The app still starts with
    /// an empty list, but it must say so: a silently empty sidebar looks
    /// identical to never having added anything, and someone who cannot
    /// glance at the window has no way to tell their repositories went
    /// missing.
    /// </summary>
    public string? LoadError { get; private set; }

    public async Task LoadAsync()
    {
        LoadError = null;

        try
        {
            if (!File.Exists(_path))
            {
                _paths = new List<string>();
                return;
            }

            await using var stream = File.OpenRead(_path);
            _paths = await JsonSerializer.DeserializeAsync<List<string>>(stream) ?? new List<string>();
        }
        catch (Exception ex)
        {
            // A corrupt or unreadable list must not stop the app starting.
            // Losing the list is recoverable by re-adding; failing to launch
            // is not. But it is reported rather than hidden.
            _paths = new List<string>();
            LoadError =
                $"Could not read your saved repository list, so it is empty. " +
                $"Add repositories again, or fix {_path}. ({ex.Message})";
        }

        // Drop anything that has been moved or deleted since last run, so the
        // user is not offered repositories that cannot be opened. Say how
        // many went, for the same reason as above.
        var before = _paths.Count;
        _paths = _paths.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        var dropped = before - _paths.Count;
        if (dropped > 0 && LoadError is null)
        {
            LoadError = dropped == 1
                ? "1 saved repository is no longer on disk and was removed from the list."
                : $"{dropped} saved repositories are no longer on disk and were removed from the list.";
        }
    }

    public async Task<bool> AddAsync(string repoPath)
    {
        var full = Path.GetFullPath(repoPath);

        if (_paths.Contains(full, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        _paths.Add(full);
        _paths.Sort((a, b) => string.Compare(
            Path.GetFileName(a), Path.GetFileName(b), StringComparison.OrdinalIgnoreCase));

        await SaveAsync();
        return true;
    }

    public async Task<bool> RemoveAsync(string repoPath)
    {
        var removed = _paths.RemoveAll(p => string.Equals(p, repoPath, StringComparison.OrdinalIgnoreCase)) > 0;
        if (removed)
        {
            await SaveAsync();
        }

        return removed;
    }

    private async Task SaveAsync()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            await using var stream = File.Create(_path);
            await JsonSerializer.SerializeAsync(stream, _paths, Options);
        }
        catch (Exception)
        {
            // Failing to persist is annoying, not fatal. The in-memory list
            // still works for this session.
        }
    }
}
