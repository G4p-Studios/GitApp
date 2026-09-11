namespace GitApp.Domain;

/// <summary>
/// The signed-in account. Kept small: this exists so the app can say who it
/// is signed in as, which is the first thing anyone needs to check when
/// something is missing that should be there.
/// </summary>
public sealed record GitHubAccount(string Login, string? Name)
{
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? Login : Name!;

    /// <summary>
    /// Both names, because the display name is what the user recognises and
    /// the login is what actually identifies the account. Someone with two
    /// accounts needs the login to tell them apart.
    /// </summary>
    public string AccessibleName =>
        string.IsNullOrWhiteSpace(Name) || Name == Login
            ? $"Signed in as {Login}"
            : $"Signed in as {Name}, {Login}";
}

/// <summary>
/// A repository on the remote, as shown in the list you pick from.
/// </summary>
public sealed record GitHubRepository(
    string Name,
    string Owner,
    string? Description,
    bool IsPrivate,
    bool IsFork,
    string? Language,
    int Stars,
    DateTimeOffset? UpdatedAt,
    string CloneUrl,
    string HtmlUrl)
{
    public string FullName => $"{Owner}/{Name}";

    /// <summary>
    /// Name first, because that is what the user is scanning for and every
    /// other field is confirmation. Visibility comes early because pushing
    /// to the wrong one of a public and a private fork matters.
    ///
    /// The description goes last, and deliberately is not truncated. A long
    /// tail costs nothing when arrowing through a list, because moving to
    /// the next row interrupts speech; a truncated sentence, on the other
    /// hand, is unreadable and there is no way to hear the rest.
    /// </summary>
    public string AccessibleName
    {
        get
        {
            var parts = new List<string>
            {
                Name,
                Owner,
                IsPrivate ? "private" : "public",
            };

            if (IsFork)
            {
                parts.Add("fork");
            }

            if (!string.IsNullOrWhiteSpace(Language))
            {
                parts.Add(Language!);
            }

            if (Stars > 0)
            {
                parts.Add(Stars == 1 ? "1 star" : $"{Stars} stars");
            }

            if (UpdatedAt is { } updated)
            {
                parts.Add($"updated {RelativeTime.From(updated)}");
            }

            if (!string.IsNullOrWhiteSpace(Description))
            {
                parts.Add(Description!.Trim());
            }

            return string.Join(", ", parts);
        }
    }

    /// <summary>The second line shown on screen, not spoken separately.</summary>
    public string Detail
    {
        get
        {
            var parts = new List<string> { IsPrivate ? "Private" : "Public" };

            if (!string.IsNullOrWhiteSpace(Language))
            {
                parts.Add(Language!);
            }

            if (UpdatedAt is { } updated)
            {
                parts.Add($"updated {RelativeTime.From(updated)}");
            }

            return string.Join(" · ", parts);
        }
    }
}
