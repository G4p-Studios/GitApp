namespace GitApp.Domain;

/// <summary>
/// A repository as the UI understands it.
///
/// Provider-agnostic on purpose: GitHub, GitLab and Codeberg all normalise
/// into this at the service boundary, so screens never see a provider-shaped
/// object. See docs/ARCHITECTURE.md section 4.1.
/// </summary>
public sealed record Repo(string Name, string Branch, int Ahead, int Behind)
{
    /// <summary>Human-readable sync state: "up to date", "2 ahead, 3 behind".</summary>
    public string Sync
    {
        get
        {
            if (Ahead == 0 && Behind == 0)
            {
                return "up to date";
            }

            var parts = new List<string>(2);
            if (Ahead > 0)
            {
                parts.Add($"{Ahead} ahead");
            }

            if (Behind > 0)
            {
                parts.Add($"{Behind} behind");
            }

            return string.Join(", ", parts);
        }
    }

    /// <summary>The secondary line shown under the name.</summary>
    public string Detail => $"{Branch} · {Sync}";

    /// <summary>
    /// The whole row as one string, in reading order.
    ///
    /// A row is announced as a single label, so the columns are flattened
    /// here rather than left for the screen reader to stitch together.
    /// See docs/ARCHITECTURE.md section 3.3.
    /// </summary>
    public string AccessibleName => $"{Name}, {Branch}, {Sync}";
}
