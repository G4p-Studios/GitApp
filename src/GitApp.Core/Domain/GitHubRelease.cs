namespace GitApp.Domain;

/// <summary>A file attached to a release.</summary>
public sealed record GitHubReleaseAsset(string Name, int Size, string DownloadUrl)
{
    public string AccessibleName => $"{Name}, {GitHubFile.FormatSize(Size)}";
}

/// <summary>
/// One release. The title leads, then the tag if it says something the
/// title does not, then the flags that change what the release means,
/// then who and when. The same content-first rule as every list here.
/// See docs/RELEASES.md.
/// </summary>
public sealed record GitHubRelease(
    string NodeId,
    string? Name,
    string TagName,
    bool IsDraft,
    bool IsPrerelease,
    bool IsLatest,
    DateTimeOffset CreatedAt,
    DateTimeOffset? PublishedAt,
    string? Author,
    string? NotesMarkdown,
    IReadOnlyList<GitHubReleaseAsset> Assets,
    int AssetTotal,
    string HtmlUrl)
{
    /// <summary>github.com shows the tag when a release was not given a name.</summary>
    public string Title => string.IsNullOrWhiteSpace(Name) ? TagName : Name!;

    public string AccessibleName
    {
        get
        {
            var parts = new List<string> { Title };

            if (!string.Equals(Title, TagName, StringComparison.Ordinal))
            {
                parts.Add($"tag {TagName}");
            }

            if (IsDraft)
            {
                parts.Add("draft");
            }

            if (IsPrerelease)
            {
                parts.Add("pre-release");
            }

            if (IsLatest)
            {
                parts.Add("latest");
            }

            if (!string.IsNullOrWhiteSpace(Author))
            {
                parts.Add($"by {Author}");
            }

            if (AssetTotal == 1)
            {
                parts.Add("1 asset");
            }
            else if (AssetTotal > 1)
            {
                parts.Add($"{AssetTotal} assets");
            }

            parts.Add(RelativeTime.From(PublishedAt ?? CreatedAt));

            return string.Join(", ", parts);
        }
    }

    /// <summary>The second visual line under the title.</summary>
    public string Detail
    {
        get
        {
            var parts = new List<string> { TagName };

            if (IsDraft)
            {
                parts.Add("draft");
            }

            if (IsPrerelease)
            {
                parts.Add("pre-release");
            }

            if (IsLatest)
            {
                parts.Add("latest");
            }

            if (!string.IsNullOrWhiteSpace(Author))
            {
                parts.Add(Author!);
            }

            parts.Add(RelativeTime.From(PublishedAt ?? CreatedAt));

            return string.Join(" · ", parts);
        }
    }

    /// <summary>The line under the details heading: state, author, date, tag.</summary>
    public string Metadata
    {
        get
        {
            var parts = new List<string>();

            parts.Add(IsDraft ? "draft" : IsPrerelease ? "pre-release" : "release");

            if (IsLatest)
            {
                parts.Add("latest");
            }

            if (!string.IsNullOrWhiteSpace(Author))
            {
                parts.Add($"by {Author}");
            }

            parts.Add(IsDraft
                ? $"created {RelativeTime.From(CreatedAt)}"
                : $"published {RelativeTime.From(PublishedAt ?? CreatedAt)}");
            parts.Add($"tag {TagName}");

            return string.Join(", ", parts);
        }
    }

    public string AssetsHeading => AssetTotal == 0
        ? "No assets"
        : AssetTotal > Assets.Count
            ? $"Assets, showing {Assets.Count} of {AssetTotal}"
            : AssetTotal == 1 ? "1 asset" : $"{AssetTotal} assets";
}

/// <summary>
/// The releases screen: the releases, and the branches a new one can be
/// cut from.
/// </summary>
public sealed record GitHubReleaseList(
    IReadOnlyList<GitHubRelease> Items,
    int TotalCount,
    IReadOnlyList<string> Branches,
    string? DefaultBranch)
{
    public bool Truncated => TotalCount > Items.Count;

    public string Summary => Items.Count == 0
        ? "No releases yet"
        : Truncated
            ? $"Showing {Items.Count} of {TotalCount} releases"
            : Items.Count == 1 ? "1 release" : $"{Items.Count} releases";
}

/// <summary>
/// What the user fills in to create a release. Validated here so the
/// message is written once and is testable.
/// </summary>
public sealed record NewRelease(
    string TagName,
    string? Target,
    string? Title,
    string? Notes,
    bool IsPrerelease,
    bool IsDraft)
{
    /// <summary>A sentence saying what is wrong, or null when it can be sent.</summary>
    public string? Problem
    {
        get
        {
            var tag = TagName.Trim();

            if (tag.Length == 0)
            {
                return "A tag is required. Version numbers like v1.2.0 are the convention.";
            }

            if (tag.Any(char.IsWhiteSpace))
            {
                return "A tag cannot contain spaces.";
            }

            if (tag.Contains("..") || tag.EndsWith('.') || tag.EndsWith(".lock") || tag.StartsWith('-'))
            {
                return $"{tag} is not a valid tag name.";
            }

            return null;
        }
    }

    public string ActionWord => IsDraft ? "Save draft" : "Publish release";
}
