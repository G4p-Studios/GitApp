namespace GitApp.Domain;

/// <summary>
/// One local branch.
/// </summary>
public sealed record BranchInfo(
    string Name,
    bool IsCurrent,
    string? Upstream,
    string TrackingSummary)
{
    /// <summary>
    /// The whole row as one string, in reading order.
    ///
    /// Name first, since that is what distinguishes one branch from another.
    /// "current" is said early because it changes what the row means, but
    /// after the name so branches do not all open with the same word.
    /// See docs/ARCHITECTURE.md 3.3.
    /// </summary>
    public string AccessibleName =>
        IsCurrent
            ? $"{Name}, current branch, {TrackingSummary}"
            : $"{Name}, {TrackingSummary}";

    public string Detail => IsCurrent ? $"current · {TrackingSummary}" : TrackingSummary;
}
