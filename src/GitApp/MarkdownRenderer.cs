namespace GitApp;

/// <summary>
/// Opens a Markdown hyperlink in the browser and says so.
///
/// The document control activates the link; this is only the launcher, so
/// a failure is spoken rather than swallowed. See docs/REPOSITORY-VIEW.md.
/// </summary>
public static class MarkdownRenderer
{
    public static async Task OpenLinkAsync(string? url)
    {
        if (string.IsNullOrEmpty(url))
        {
            return;
        }

        try
        {
            await Launcher.Default.OpenAsync(url);
            Accessibility.Announcer.Current.Announce($"Opened {url} in your browser");
        }
        catch (Exception)
        {
            Accessibility.Announcer.Current.Announce("Could not open your browser.");
        }
    }
}
