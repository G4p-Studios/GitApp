using System.Text;
using System.Text.Json;
using GitApp.Domain;

namespace GitApp.GitHub;

/// <summary>
/// Releases. The list is GraphQL, one request for the releases and the
/// branches a new one can target. Creating one is REST: GraphQL has no
/// mutation for it. See docs/RELEASES.md.
/// </summary>
public sealed partial class GitHubClient
{
    private const string ReleasesQuery = """
        query Releases($owner: String!, $name: String!) {
          repository(owner: $owner, name: $name) {
            defaultBranchRef { name }
            refs(refPrefix: "refs/heads/", first: 100, orderBy: {field: ALPHABETICAL, direction: ASC}) {
              nodes { name }
            }
            releases(first: 50, orderBy: {field: CREATED_AT, direction: DESC}) {
              totalCount
              nodes {
                id
                name
                tagName
                isDraft
                isPrerelease
                isLatest
                createdAt
                publishedAt
                url
                description
                author { login }
                releaseAssets(first: 30) {
                  totalCount
                  nodes { name size downloadUrl }
                }
              }
            }
          }
        }
        """;

    public async Task<GitHubResult<GitHubReleaseList>> GetReleasesAsync(
        string owner, string name, CancellationToken ct = default)
    {
        var result = await GraphqlAsync(
            ReleasesQuery,
            new Dictionary<string, object?> { ["owner"] = owner, ["name"] = name },
            ct);

        if (!result.Success)
        {
            return GitHubResult<GitHubReleaseList>.Fail(result.Error!, result.Failure);
        }

        try
        {
            var list = ParseReleases(result.Value!);
            return list is null
                ? GitHubResult<GitHubReleaseList>.Fail(
                    "GitHub could not find that. It may be private, renamed, or deleted.",
                    GitHubFailure.NotFound)
                : GitHubResult<GitHubReleaseList>.Ok(list);
        }
        catch (JsonException)
        {
            return GitHubResult<GitHubReleaseList>.Fail("GitHub sent a reply GitApp could not read.");
        }
    }

    /// <summary>Turn the GraphQL reply into ours. Public for tests.</summary>
    public static GitHubReleaseList? ParseReleases(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (!TryRepo(document.RootElement, out var repo))
        {
            return null;
        }

        string? defaultBranch = null;
        if (repo.TryGetProperty("defaultBranchRef", out var def) && def.ValueKind == JsonValueKind.Object)
        {
            defaultBranch = String(def, "name");
        }

        var branches = Names(repo, "refs", "name");

        var items = new List<GitHubRelease>();
        var total = 0;
        if (repo.TryGetProperty("releases", out var releases) && releases.ValueKind == JsonValueKind.Object)
        {
            total = Int(releases, "totalCount");
            if (releases.TryGetProperty("nodes", out var nodes) && nodes.ValueKind == JsonValueKind.Array)
            {
                foreach (var node in nodes.EnumerateArray())
                {
                    if (ReadRelease(node) is { } release)
                    {
                        items.Add(release);
                    }
                }
            }
        }

        return new GitHubReleaseList(items, total, branches, defaultBranch);
    }

    internal static GitHubRelease? ReadRelease(JsonElement node)
    {
        var tag = String(node, "tagName");
        if (string.IsNullOrEmpty(tag))
        {
            return null;
        }

        var assets = new List<GitHubReleaseAsset>();
        var assetTotal = 0;
        if (node.TryGetProperty("releaseAssets", out var wrap) && wrap.ValueKind == JsonValueKind.Object)
        {
            assetTotal = Int(wrap, "totalCount");
            if (wrap.TryGetProperty("nodes", out var assetNodes) && assetNodes.ValueKind == JsonValueKind.Array)
            {
                foreach (var asset in assetNodes.EnumerateArray())
                {
                    if (String(asset, "name") is { Length: > 0 } assetName)
                    {
                        assets.Add(new GitHubReleaseAsset(
                            assetName,
                            Int(asset, "size"),
                            String(asset, "downloadUrl") ?? string.Empty));
                    }
                }
            }
        }

        return new GitHubRelease(
            NodeId: String(node, "id") ?? string.Empty,
            Name: String(node, "name"),
            TagName: tag,
            IsDraft: Bool(node, "isDraft"),
            IsPrerelease: Bool(node, "isPrerelease"),
            IsLatest: Bool(node, "isLatest"),
            CreatedAt: Date(node, "createdAt") ?? DateTimeOffset.UtcNow,
            PublishedAt: Date(node, "publishedAt"),
            Author: ActorLogin(node),
            NotesMarkdown: String(node, "description"),
            Assets: assets,
            AssetTotal: assetTotal,
            HtmlUrl: String(node, "url") ?? string.Empty);
    }

    /// <summary>
    /// Create a release over REST. The tag is created on the target if it
    /// does not exist yet, which is github.com's behaviour too.
    /// </summary>
    public async Task<GitHubResult<GitHubRelease>> CreateReleaseAsync(
        string owner, string name, NewRelease release, CancellationToken ct = default)
    {
        if (release.Problem is { } problem)
        {
            return GitHubResult<GitHubRelease>.Fail(problem);
        }

        var payload = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["tag_name"] = release.TagName.Trim(),
            ["target_commitish"] = string.IsNullOrWhiteSpace(release.Target) ? null : release.Target,
            ["name"] = string.IsNullOrWhiteSpace(release.Title) ? null : release.Title!.Trim(),
            ["body"] = string.IsNullOrWhiteSpace(release.Notes) ? null : release.Notes,
            ["draft"] = release.IsDraft,
            ["prerelease"] = release.IsPrerelease,
        }.Where(kv => kv.Value is not null).ToDictionary(kv => kv.Key, kv => kv.Value));

        using var body = new StringContent(payload, Encoding.UTF8, "application/json");
        var result = await SendAsync(HttpMethod.Post, $"{ApiRoot}/repos/{owner}/{name}/releases", body, ct);

        if (!result.Success)
        {
            return GitHubResult<GitHubRelease>.Fail(DescribeReleaseFailure(result), result.Failure);
        }

        try
        {
            var created = ParseCreatedRelease(result.Value!);
            return created is null
                ? GitHubResult<GitHubRelease>.Fail(
                    "GitHub did not confirm the release. Refresh the list to check whether it was created.")
                : GitHubResult<GitHubRelease>.Ok(created);
        }
        catch (JsonException)
        {
            return GitHubResult<GitHubRelease>.Fail(
                "GitHub sent a reply GitApp could not read. Refresh the list to check whether the release was created.");
        }
    }

    /// <summary>
    /// REST's 422 for a release is almost always one of two things, and
    /// its message, "Validation Failed", names neither.
    /// </summary>
    private static string DescribeReleaseFailure(GitHubResult<string> result)
    {
        var error = result.Error ?? string.Empty;

        return error.Contains("(422)", StringComparison.Ordinal)
            ? "GitHub refused the release. A release with that tag may already exist, or the target branch may not."
            : error;
    }

    /// <summary>The REST release object as ours. Public for tests.</summary>
    public static GitHubRelease? ParseCreatedRelease(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        var tag = String(root, "tag_name");
        if (root.ValueKind != JsonValueKind.Object || string.IsNullOrEmpty(tag))
        {
            return null;
        }

        return new GitHubRelease(
            NodeId: String(root, "node_id") ?? string.Empty,
            Name: String(root, "name"),
            TagName: tag,
            IsDraft: Bool(root, "draft"),
            IsPrerelease: Bool(root, "prerelease"),
            IsLatest: false,
            CreatedAt: Date(root, "created_at") ?? DateTimeOffset.UtcNow,
            PublishedAt: Date(root, "published_at"),
            Author: root.TryGetProperty("author", out var author) && author.ValueKind == JsonValueKind.Object
                ? String(author, "login")
                : null,
            NotesMarkdown: String(root, "body"),
            Assets: Array.Empty<GitHubReleaseAsset>(),
            AssetTotal: 0,
            HtmlUrl: String(root, "html_url") ?? string.Empty);
    }
}
