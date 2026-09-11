using System.Text.Json;
using GitApp.Domain;

namespace GitApp.GitHub;

/// <summary>
/// One remote file, as a blob.
///
/// GraphQL rather than REST contents: the same client already speaks
/// GraphQL for the tree, and <c>object(expression:)</c> returns text,
/// whether it is binary, and the size in one response. GitHub withholds
/// <c>text</c> when the blob is binary or larger than about a megabyte;
/// those are spoken empty states, not silent ones. See
/// docs/REPOSITORY-VIEW.md.
/// </summary>
public sealed partial class GitHubClient
{
    private const string FileQuery = """
        query FileBlob($owner: String!, $name: String!, $expression: String!) {
          repository(owner: $owner, name: $name) {
            object(expression: $expression) {
              ... on Blob {
                byteSize
                isBinary
                text
              }
            }
          }
        }
        """;

    public async Task<GitHubResult<GitHubFile>> GetFileAsync(
        string owner,
        string name,
        string branch,
        string path,
        string repoHtmlUrl,
        CancellationToken ct = default)
    {
        var refName = string.IsNullOrWhiteSpace(branch) ? "HEAD" : branch;
        var result = await GraphqlAsync(
            FileQuery,
            new Dictionary<string, object?>
            {
                ["owner"] = owner,
                ["name"] = name,
                ["expression"] = FileExpression(refName, path),
            },
            ct);

        if (!result.Success)
        {
            return GitHubResult<GitHubFile>.Fail(result.Error!, result.Failure);
        }

        try
        {
            var file = ParseFile(result.Value!, path, refName, repoHtmlUrl);
            return file is null
                ? GitHubResult<GitHubFile>.Fail(
                    "GitHub could not find that file. It may have been moved or deleted.",
                    GitHubFailure.NotFound)
                : GitHubResult<GitHubFile>.Ok(file);
        }
        catch (JsonException)
        {
            return GitHubResult<GitHubFile>.Fail("GitHub sent a reply GitApp could not read.");
        }
    }

    /// <summary>Turn a GraphQL blob into ours. Public for tests.</summary>
    public static GitHubFile? ParseFile(
        string json, string path, string branch, string repoHtmlUrl)
    {
        using var document = JsonDocument.Parse(json);
        if (!TryRepo(document.RootElement, out var repo)
            || !repo.TryGetProperty("object", out var blob)
            || blob.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        // A folder matches the expression as a Tree. The Blob fragment then
        // contributes nothing, and treating that as "too large" would lie.
        if (!blob.TryGetProperty("byteSize", out _) && !blob.TryGetProperty("isBinary", out _))
        {
            return null;
        }

        var isBinary = Bool(blob, "isBinary");
        var text = String(blob, "text");
        var tooLarge = !isBinary && text is null;
        var normalised = NormalizePath(path);
        var fileName = System.IO.Path.GetFileName(normalised);
        if (string.IsNullOrEmpty(fileName))
        {
            fileName = normalised;
        }

        return new GitHubFile(
            fileName,
            normalised,
            branch,
            Int(blob, "byteSize"),
            isBinary,
            tooLarge,
            text,
            GitHubFile.SplitLines(text),
            GitHubFile.IsMarkdownName(fileName),
            GitHubFile.BlobUrl(repoHtmlUrl, branch, normalised));
    }

    internal static string FileExpression(string branch, string path) =>
        $"{branch}:{NormalizePath(path)}";
}
