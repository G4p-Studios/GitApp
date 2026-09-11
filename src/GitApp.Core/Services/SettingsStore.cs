using System.Text.Json;
using GitApp.Domain;

namespace GitApp.Services;

/// <summary>
/// The handful of preferences that change how much the app says.
///
/// These are accessibility settings, not cosmetics. How much context a diff
/// carries and how big a hunk has to be before it folds both decide how long
/// it takes to listen to a change, and the right answer differs enormously
/// between someone reading at 200 words a minute and someone reading at 700.
/// </summary>
public sealed class AppSettings
{
    /// <summary>
    /// Unchanged lines shown either side of a change. Git's default is 3,
    /// which is a reasonable compromise: enough to recognise where you are,
    /// short enough not to bury the change itself.
    /// </summary>
    public int DiffContextLines { get; set; } = 3;

    /// <summary>Hunks longer than this start folded.</summary>
    public int LargeHunkLines { get; set; } = DiffRow.DefaultLargeHunkLines;

    /// <summary>
    /// The OAuth client ID for browser sign-in.
    ///
    /// Empty by default, because GitApp has no registered GitHub OAuth
    /// application yet and borrowing another product's client ID would mean
    /// users granting access to something that is not this app. Until one
    /// is registered, sign-in is by personal access token and the app says
    /// so rather than offering a button that cannot work. Anyone can put
    /// their own here: register an OAuth app on GitHub with device flow
    /// enabled and paste its client ID.
    /// </summary>
    public string? GitHubClientId { get; set; }

    /// <summary>
    /// Keep values in a range the UI can actually present. A context of a
    /// thousand is not a preference, it is a corrupt file.
    /// </summary>
    public AppSettings Clamped() => new()
    {
        DiffContextLines = Math.Clamp(DiffContextLines, 0, 50),
        LargeHunkLines = Math.Clamp(LargeHunkLines, 5, 1000),
        GitHubClientId = string.IsNullOrWhiteSpace(GitHubClientId) ? null : GitHubClientId.Trim(),
    };
}

/// <summary>
/// Preferences as plain JSON beside the repository list. Same shape, same
/// rule: a failure to read is reported, never swallowed.
/// </summary>
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private readonly string _path;

    public SettingsStore(string? filePath = null)
    {
        _path = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "GitApp",
            "settings.json");
    }

    public AppSettings Settings { get; private set; } = new();

    /// <summary>Set when the saved preferences could not be read.</summary>
    public string? LoadError { get; private set; }

    public async Task LoadAsync()
    {
        LoadError = null;

        try
        {
            if (!File.Exists(_path))
            {
                Settings = new AppSettings();
                return;
            }

            await using var stream = File.OpenRead(_path);
            var loaded = await JsonSerializer.DeserializeAsync<AppSettings>(stream);
            Settings = (loaded ?? new AppSettings()).Clamped();
        }
        catch (Exception ex)
        {
            Settings = new AppSettings();
            LoadError =
                $"Could not read your settings, so the defaults are in use. " +
                $"Fix or delete {_path}. ({ex.Message})";
        }
    }

    public async Task SaveAsync()
    {
        Settings = Settings.Clamped();

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            await using var stream = File.Create(_path);
            await JsonSerializer.SerializeAsync(stream, Settings, Options);
        }
        catch (Exception)
        {
            // Failing to persist is annoying, not fatal: the change still
            // applies for this session.
        }
    }
}
