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

    public async Task LoadAsync()
    {
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
        catch (Exception)
        {
            // A corrupt or unreadable list must not stop the app starting.
            // Losing the list is recoverable by re-adding; failing to launch
            // is not.
            _paths = new List<string>();
        }

        // Drop anything that has been moved or deleted since last run, so the
        // user is not offered repositories that cannot be opened.
        _paths = _paths.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
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
