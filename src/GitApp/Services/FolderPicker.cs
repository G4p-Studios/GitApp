namespace GitApp.Services;

/// <summary>
/// Choosing a folder.
///
/// MAUI has a file picker but no folder picker, and adding
/// CommunityToolkit.Maui for one dialog is a poor trade. Each platform gets a
/// small implementation instead.
///
/// The system dialog is the right answer rather than a custom one: it is
/// already keyboard accessible, already known to screen reader users, and
/// already supports the shortcuts and places they have configured.
/// </summary>
public static partial class FolderPicker
{
    /// <summary>
    /// Returns the chosen folder path, or null when cancelled or when the
    /// platform has no implementation.
    /// </summary>
    public static Task<string?> PickAsync()
    {
        Task<string?>? result = null;
        PickPlatform(ref result);
        return result ?? Task.FromResult<string?>(null);
    }

    static partial void PickPlatform(ref Task<string?>? result);
}
