using System.Text;
using System.Text.Json;
using GitApp.Domain;

namespace GitApp.GitHub;

/// <summary>
/// The repository view, over GraphQL.
///
/// REST cannot return the last commit that touched each file in one
/// request, and that is the column github.com shows next to every name.
/// GraphQL can, as a second query of aliased <c>history(path:)</c> fields
/// against the same commit. The file table is not shown until both
/// replies are in, so the list does not rebuild under the user's cursor.
/// See docs/REPOSITORY-VIEW.md and ARCHITECTURE 4.3.
/// </summary>
public sealed partial class GitHubClient
{
    private const int LastCommitChunk = 40;

    private const string RepositoryQuery = """
        query RepositoryView(
          $owner: String!,
          $name: String!,
          $qualifiedRef: String!,
          $treeExpression: String!
        ) {
          repository(owner: $owner, name: $name) {
            name
            description
            isPrivate
            stargazerCount
            forkCount
            homepageUrl
            url
            owner { login }
            primaryLanguage { name }
            watchers { totalCount }
            repositoryTopics(first: 20) {
              nodes { topic { name } }
            }
            defaultBranchRef { name }
            refs(refPrefix: "refs/heads/", first: 100) {
              totalCount
              nodes { name }
            }
            tags: refs(refPrefix: "refs/tags/", first: 1) {
              totalCount
            }
            releases { totalCount }
            openIssues: issues(states: OPEN) { totalCount }
            openPullRequests: pullRequests(states: OPEN) { totalCount }
            object(expression: $qualifiedRef) {
              ... on Commit {
                oid
                abbreviatedOid
                messageHeadline
                committedDate
                authors(first: 8) {
                  nodes { name user { login } }
                }
                history { totalCount }
              }
            }
            tree: object(expression: $treeExpression) {
              ... on Tree {
                entries {
                  name
                  type
                  path
                }
              }
            }
          }
        }
        """;

    /// <summary>
    /// One snapshot of a repository at a branch and path.
    ///
    /// <paramref name="branch"/> null means HEAD, which follows the default
    /// branch. Empty <paramref name="path"/> is the repository root.
    /// </summary>
    public async Task<GitHubResult<GitHubRepoView>> GetRepositoryViewAsync(
        string owner,
        string name,
        string? branch = null,
        string path = "",
        CancellationToken ct = default)
    {
        var refExpr = string.IsNullOrWhiteSpace(branch) ? "HEAD" : branch.Trim();
        var pathNorm = NormalizePath(path);
        var treeExpr = string.IsNullOrEmpty(pathNorm) ? $"{refExpr}:" : $"{refExpr}:{pathNorm}";

        var first = await GraphqlAsync(
            RepositoryQuery,
            new Dictionary<string, object?>
            {
                ["owner"] = owner,
                ["name"] = name,
                ["qualifiedRef"] = refExpr,
                ["treeExpression"] = treeExpr,
            },
            ct);

        if (!first.Success)
        {
            return GitHubResult<GitHubRepoView>.Fail(first.Error!, first.Failure);
        }

        GitHubRepoView snapshot;
        try
        {
            if (ParseRepository(first.Value!, owner, pathNorm, branch) is not { } parsed)
            {
                return GitHubResult<GitHubRepoView>.Fail(
                    "GitHub could not find that. It may be private, renamed, or deleted.",
                    GitHubFailure.NotFound);
            }

            snapshot = parsed;
        }
        catch (JsonException)
        {
            return GitHubResult<GitHubRepoView>.Fail("GitHub sent a reply GitApp could not read.");
        }

        var currentRef = snapshot.CurrentBranch;
        var details = await FetchEntryDetailsAsync(
            owner,
            name,
            snapshot.HeadOid,
            snapshot.Entries,
            currentRef,
            pathNorm,
            ct);

        if (details is null)
        {
            return GitHubResult<GitHubRepoView>.Ok(snapshot);
        }

        return GitHubResult<GitHubRepoView>.Ok(snapshot with
        {
            Entries = details.Value.Entries,
            ReadmeName = details.Value.ReadmeName,
            ReadmeMarkdown = details.Value.ReadmeMarkdown,
        });
    }

    /// <summary>Turn the GraphQL repository payload into ours. Public for tests.</summary>
    public static GitHubRepoView? ParseRepository(
        string json,
        string? ownerFallback = null,
        string path = "",
        string? branch = null)
    {
        using var document = JsonDocument.Parse(json);

        if (!TryRepo(document.RootElement, out var repo) || repo.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var name = String(repo, "name") ?? string.Empty;
        var owner = ownerFallback
            ?? (repo.TryGetProperty("owner", out var ownerEl) ? String(ownerEl, "login") : null)
            ?? string.Empty;
        var defaultBranch = repo.TryGetProperty("defaultBranchRef", out var defaultRef)
            ? String(defaultRef, "name")
            : null;
        var currentBranch = string.IsNullOrWhiteSpace(branch) || branch == "HEAD"
            ? defaultBranch ?? "HEAD"
            : branch.Trim();

        var branchNodes = Array.Empty<JsonElement>();
        var branchCount = 0;
        if (repo.TryGetProperty("refs", out var refs) && refs.ValueKind == JsonValueKind.Object)
        {
            branchCount = Int(refs, "totalCount");
            if (refs.TryGetProperty("nodes", out var nodes) && nodes.ValueKind == JsonValueKind.Array)
            {
                branchNodes = nodes.EnumerateArray().ToArray();
            }
        }

        var branches = branchNodes
            .Select(n => String(n, "name"))
            .Where(n => !string.IsNullOrEmpty(n))
            .Select(n => new GitHubBranch(n!, n == defaultBranch))
            .ToList();

        string? oid = null;
        GitHubLatestCommit? latest = null;

        if (repo.TryGetProperty("object", out var commit) && commit.ValueKind == JsonValueKind.Object)
        {
            oid = String(commit, "oid");
            var shortHash = String(commit, "abbreviatedOid") ?? (oid is { Length: >= 7 } ? oid[..7] : oid ?? string.Empty);
            var subject = String(commit, "messageHeadline") ?? string.Empty;
            var when = Date(commit, "committedDate") ?? DateTimeOffset.UtcNow;
            var authors = ReadAuthors(commit);
            var count = 0;
            if (commit.TryGetProperty("history", out var history))
            {
                count = Int(history, "totalCount");
            }

            if (subject.Length > 0 || shortHash.Length > 0)
            {
                latest = new GitHubLatestCommit(shortHash, subject, authors, when, count, currentBranch);
            }
        }

        var entries = new List<GitHubTreeEntry>();
        if (repo.TryGetProperty("tree", out var tree)
            && tree.ValueKind == JsonValueKind.Object
            && tree.TryGetProperty("entries", out var treeEntries)
            && treeEntries.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in treeEntries.EnumerateArray())
            {
                var entryName = String(item, "name");
                if (string.IsNullOrEmpty(entryName))
                {
                    continue;
                }

                entries.Add(new GitHubTreeEntry(
                    entryName,
                    KindOf(String(item, "type")),
                    String(item, "path") ?? entryName));
            }
        }

        var topics = new List<string>();
        if (repo.TryGetProperty("repositoryTopics", out var topicWrap)
            && topicWrap.TryGetProperty("nodes", out var topicNodes)
            && topicNodes.ValueKind == JsonValueKind.Array)
        {
            foreach (var node in topicNodes.EnumerateArray())
            {
                if (node.TryGetProperty("topic", out var topic)
                    && String(topic, "name") is { Length: > 0 } topicName)
                {
                    topics.Add(topicName);
                }
            }
        }

        var pathNorm = NormalizePath(path);
        var sorted = SortEntries(entries, pathNorm);

        var tagCount = 0;
        if (repo.TryGetProperty("tags", out var tags))
        {
            tagCount = Int(tags, "totalCount");
        }

        var releaseCount = 0;
        if (repo.TryGetProperty("releases", out var releases))
        {
            releaseCount = Int(releases, "totalCount");
        }

        var openIssues = 0;
        if (repo.TryGetProperty("openIssues", out var openIssuesEl))
        {
            openIssues = Int(openIssuesEl, "totalCount");
        }

        var openPulls = 0;
        if (repo.TryGetProperty("openPullRequests", out var openPullsEl))
        {
            openPulls = Int(openPullsEl, "totalCount");
        }

        var watchers = 0;
        if (repo.TryGetProperty("watchers", out var watchersEl))
        {
            watchers = Int(watchersEl, "totalCount");
        }

        var language = repo.TryGetProperty("primaryLanguage", out var lang)
            ? String(lang, "name")
            : null;

        return new GitHubRepoView(
            Name: name,
            Owner: owner,
            Description: String(repo, "description"),
            IsPrivate: Bool(repo, "isPrivate"),
            Topics: topics,
            Language: language,
            Stars: Int(repo, "stargazerCount"),
            Watchers: watchers,
            Forks: Int(repo, "forkCount"),
            ReleaseCount: releaseCount,
            BranchCount: branchCount == 0 ? branches.Count : branchCount,
            TagCount: tagCount,
            OpenIssueCount: openIssues,
            OpenPullRequestCount: openPulls,
            HomepageUrl: String(repo, "homepageUrl"),
            HtmlUrl: String(repo, "url") ?? string.Empty,
            CurrentBranch: currentBranch,
            DefaultBranch: defaultBranch,
            Path: pathNorm,
            HeadOid: oid,
            Latest: latest,
            Branches: branches,
            Entries: sorted,
            ReadmeName: null,
            ReadmeMarkdown: null);
    }

    /// <summary>
    /// Directories first, then submodules, then files, each group
    /// alphabetical. github.com groups them that way; saying "folder" or
    /// "file" on every row makes the grouping audible as well, but matching
    /// the visual order means a sighted user and a screen reader user are
    /// pointing at the same thing when they say "the third row".
    /// </summary>
    public static IReadOnlyList<GitHubTreeEntry> SortEntries(
        IEnumerable<GitHubTreeEntry> entries, string path = "")
    {
        var sorted = entries
            .Where(e => e.Kind != GitHubEntryKind.Parent)
            .OrderBy(e => e.Kind switch
            {
                GitHubEntryKind.Folder => 0,
                GitHubEntryKind.Submodule => 1,
                _ => 2,
            })
            .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (!string.IsNullOrEmpty(path))
        {
            sorted.Insert(0, GitHubTreeEntry.Parent(ParentPath(path)));
        }

        return sorted;
    }

    /// <summary>
    /// The README filename we would open from this tree, if any.
    ///
    /// Preference matches github.com: README.md before README, case
    /// insensitive, files only. A folder named README is not a readme.
    /// </summary>
    public static GitHubTreeEntry? FindReadme(IEnumerable<GitHubTreeEntry> entries)
    {
        var files = entries
            .Where(e => e.Kind == GitHubEntryKind.File)
            .ToList();

        foreach (var candidate in new[]
        {
            "README.md", "README.markdown", "README.mdown", "README.rst", "README.txt", "README",
        })
        {
            var match = files.FirstOrDefault(e =>
                string.Equals(e.Name, candidate, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                return match;
            }
        }

        return null;
    }

    /// <summary>Build the aliased history query. Public for tests.</summary>
    public static string LastCommitQuery(IReadOnlyList<string> paths, bool withReadme)
    {
        var sb = new StringBuilder();
        sb.Append("query LastTouches($owner: String!, $name: String!");
        if (paths.Count > 0)
        {
            sb.Append(", $oid: GitObjectID!");
        }

        if (withReadme)
        {
            sb.Append(", $readmeExpression: String!");
        }

        sb.Append(") { repository(owner: $owner, name: $name) { ");

        if (withReadme)
        {
            sb.Append("readme: object(expression: $readmeExpression) { ... on Blob { text isBinary } } ");
        }

        if (paths.Count > 0)
        {
            sb.Append("object(oid: $oid) { ... on Commit { ");
            for (var i = 0; i < paths.Count; i++)
            {
                sb.Append('p').Append(i)
                    .Append(": history(first: 1, path: \"")
                    .Append(EscapeGraphql(paths[i]))
                    .Append("\") { nodes { messageHeadline committedDate } } ");
            }

            sb.Append("} } ");
        }

        sb.Append("} }");
        return sb.ToString();
    }

    /// <summary>Apply last-touch subjects onto tree rows. Public for tests.</summary>
    public static IReadOnlyList<GitHubTreeEntry> ApplyLastCommits(
        IReadOnlyList<GitHubTreeEntry> entries,
        IReadOnlyDictionary<string, LastTouch> last)
    {
        return entries.Select(e =>
            last.TryGetValue(e.Path, out var touch)
                ? e with { LastSubject = touch.Subject, LastTouched = touch.When }
                : e).ToList();
    }

    public readonly record struct LastTouch(string Subject, DateTimeOffset? When);

    private async Task<(IReadOnlyList<GitHubTreeEntry> Entries, string? ReadmeName, string? ReadmeMarkdown)?> FetchEntryDetailsAsync(
        string owner,
        string name,
        string? oid,
        IReadOnlyList<GitHubTreeEntry> entries,
        string branch,
        string path,
        CancellationToken ct)
    {
        var readme = FindReadme(entries);
        var real = entries.Where(e => e.Kind != GitHubEntryKind.Parent).ToList();

        if (readme is null && (oid is null || real.Count == 0))
        {
            return null;
        }

        var last = new Dictionary<string, LastTouch>();
        string? readmeText = null;
        var readmeName = readme?.Name;

        if (oid is not null && real.Count > 0)
        {
            for (var offset = 0; offset < real.Count; offset += LastCommitChunk)
            {
                var chunk = real.Skip(offset).Take(LastCommitChunk).ToList();
                var fetchReadme = offset == 0 && readme is not null;
                var query = LastCommitQuery(chunk.Select(e => e.Path).ToList(), fetchReadme);

                var variables = new Dictionary<string, object?>
                {
                    ["owner"] = owner,
                    ["name"] = name,
                    ["oid"] = oid,
                };

                if (fetchReadme)
                {
                    variables["readmeExpression"] = ReadmeExpression(branch, path, readme!.Name);
                }

                var reply = await GraphqlAsync(query, variables, ct);
                if (!reply.Success)
                {
                    // Last-touch is confirmation, not the table. A failure
                    // here must not hide the files we already have.
                    break;
                }

                try
                {
                    MergeDetails(reply.Value!, chunk, last, ref readmeText);
                }
                catch (JsonException)
                {
                    break;
                }
            }
        }
        else if (readme is not null)
        {
            var query = LastCommitQuery(Array.Empty<string>(), withReadme: true);
            var reply = await GraphqlAsync(query, new Dictionary<string, object?>
            {
                ["owner"] = owner,
                ["name"] = name,
                ["readmeExpression"] = ReadmeExpression(branch, path, readme.Name),
            }, ct);

            if (reply.Success)
            {
                try
                {
                    MergeDetails(reply.Value!, Array.Empty<GitHubTreeEntry>(), last, ref readmeText);
                }
                catch (JsonException)
                {
                    // Same rule: a bad readme is an empty readme pane, not a failed screen.
                }
            }
        }

        return (ApplyLastCommits(entries, last), readmeName, readmeText);
    }

    internal static void MergeDetails(
        string json,
        IReadOnlyList<GitHubTreeEntry> chunk,
        Dictionary<string, LastTouch> last,
        ref string? readmeText)
    {
        using var document = JsonDocument.Parse(json);
        if (!TryRepo(document.RootElement, out var repo))
        {
            return;
        }

        if (readmeText is null
            && repo.TryGetProperty("readme", out var readme)
            && readme.ValueKind == JsonValueKind.Object
            && !Bool(readme, "isBinary"))
        {
            readmeText = String(readme, "text");
        }

        if (!repo.TryGetProperty("object", out var commit) || commit.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        for (var i = 0; i < chunk.Count; i++)
        {
            if (!commit.TryGetProperty("p" + i, out var history))
            {
                continue;
            }

            if (!history.TryGetProperty("nodes", out var nodes) || nodes.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            var node = nodes.EnumerateArray().FirstOrDefault();
            if (node.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var subject = String(node, "messageHeadline");
            if (string.IsNullOrEmpty(subject))
            {
                continue;
            }

            last[chunk[i].Path] = new LastTouch(subject, Date(node, "committedDate"));
        }
    }

    private async Task<GitHubResult<string>> GraphqlAsync(
        string query,
        IReadOnlyDictionary<string, object?> variables,
        CancellationToken ct)
    {
        var payload = JsonSerializer.Serialize(
            new { query, variables },
            new JsonSerializerOptions { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull });

        using var body = new StringContent(payload, Encoding.UTF8, "application/json");
        var result = await SendAsync(HttpMethod.Post, $"{ApiRoot}/graphql", body, ct);
        if (!result.Success)
        {
            return result;
        }

        try
        {
            using var document = JsonDocument.Parse(result.Value!);
            if (GraphqlMessage(document.RootElement, out var failure) is { } message
                && !HasRepository(document.RootElement))
            {
                return GitHubResult<string>.Fail(message, failure);
            }
        }
        catch (JsonException)
        {
            return GitHubResult<string>.Fail("GitHub sent a reply GitApp could not read.");
        }

        return result;
    }

    internal static string? GraphqlMessage(JsonElement root, out GitHubFailure failure)
    {
        failure = GitHubFailure.Other;

        if (!root.TryGetProperty("errors", out var errors) || errors.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var first = errors.EnumerateArray().FirstOrDefault();
        if (first.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var type = String(first, "type");
        failure = type is "NOT_FOUND" or "NOT_FOUND_ERROR"
            ? GitHubFailure.NotFound
            : type is "FORBIDDEN" or "INSUFFICIENT_SCOPES"
                ? GitHubFailure.Forbidden
                : GitHubFailure.Other;

        return String(first, "message")
            ?? "GitHub could not complete that request.";
    }

    private static bool HasRepository(JsonElement root) =>
        TryRepo(root, out var repo) && repo.ValueKind == JsonValueKind.Object;

    private static bool TryRepo(JsonElement root, out JsonElement repo)
    {
        repo = default;
        return root.TryGetProperty("data", out var data)
            && data.ValueKind == JsonValueKind.Object
            && data.TryGetProperty("repository", out repo)
            && repo.ValueKind == JsonValueKind.Object;
    }

    internal static IReadOnlyList<string> ReadAuthors(JsonElement commit)
    {
        var names = new List<string>();

        if (commit.TryGetProperty("authors", out var authors)
            && authors.TryGetProperty("nodes", out var nodes)
            && nodes.ValueKind == JsonValueKind.Array)
        {
            foreach (var node in nodes.EnumerateArray())
            {
                var login = node.TryGetProperty("user", out var user)
                    ? String(user, "login")
                    : null;
                var name = login ?? String(node, "name");
                if (!string.IsNullOrWhiteSpace(name) && !names.Contains(name))
                {
                    names.Add(name);
                }
            }
        }

        return names;
    }

    internal static GitHubEntryKind KindOf(string? type) => type switch
    {
        "tree" => GitHubEntryKind.Folder,
        "commit" => GitHubEntryKind.Submodule,
        _ => GitHubEntryKind.File,
    };

    internal static string NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || path == "/" || path == ".")
        {
            return string.Empty;
        }

        return path.Replace('\\', '/').Trim('/');
    }

    internal static string ParentPath(string path)
    {
        var slash = path.Replace('\\', '/').Trim('/').LastIndexOf('/');
        return slash < 0 ? string.Empty : path[..slash];
    }

    internal static string ReadmeExpression(string branch, string path, string readmeName) =>
        string.IsNullOrEmpty(path) ? $"{branch}:{readmeName}" : $"{branch}:{path}/{readmeName}";

    internal static string EscapeGraphql(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
}
