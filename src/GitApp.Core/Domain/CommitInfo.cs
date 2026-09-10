namespace GitApp.Domain;

/// <summary>
/// One commit, as shown in a history list.
/// </summary>
public sealed record CommitInfo(string ShortHash, string Author, string RelativeDate, string Subject)
{
    /// <summary>
    /// The whole row as one string, in reading order.
    ///
    /// Subject first: it is what distinguishes one commit from another, and
    /// leading with the hash would make every row open with a meaningless
    /// string of characters read letter by letter. See
    /// docs/ARCHITECTURE.md 3.3.
    /// </summary>
    public string AccessibleName => $"{Subject}, by {Author}, {RelativeDate}, {ShortHash}";

    public string Detail => $"{Author} · {RelativeDate} · {ShortHash}";
}
