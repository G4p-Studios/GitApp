using GitApp.Domain;

namespace GitApp.Services;

/// <summary>
/// Branching, cloning and conflict resolution.
/// </summary>
public sealed partial class GitService
{
    // -----------------------------------------------------------------
    // Branches
    // -----------------------------------------------------------------

    /// <summary>
    /// Local branches, with the current one marked.
    ///
    /// for-each-ref rather than "git branch": the latter is a porcelain
    /// command whose output is meant for people, decorated with an asterisk
    /// and indentation that would have to be stripped.
    /// </summary>
    public async Task<IReadOnlyList<BranchInfo>> GetBranchDetailsAsync(
        string repoPath,
        CancellationToken ct = default)
    {
        // Unit separator: cannot occur in a ref name, so no quoting is
        // needed. Written as an escape rather than a literal control
        // byte so the format survives an editor and a diff.
        const char Sep = '\u001f';

        var result = await _git.RunAsync(
            repoPath,
            new[]
            {
                "for-each-ref",
                $"--format=%(refname:short){Sep}%(HEAD){Sep}%(upstream:short){Sep}%(upstream:track)",
                "refs/heads",
            },
            ct);

        if (!result.Success)
        {
            return Array.Empty<BranchInfo>();
        }

        return result.Lines
            .Select(line => line.Split(Sep))
            .Where(parts => parts.Length >= 4 && parts[0].Length > 0)
            .Select(parts => new BranchInfo(
                Name: parts[0],
                IsCurrent: parts[1] == "*",
                Upstream: string.IsNullOrEmpty(parts[2]) ? null : parts[2],
                TrackingSummary: ParseTrack(parts[3])))
            .ToList();
    }

    /// <summary>
    /// Turn git's "[ahead 2, behind 1]" into words. Symbols and brackets read
    /// poorly and inconsistently depending on punctuation level.
    /// </summary>
    private static string ParseTrack(string track)
    {
        if (string.IsNullOrWhiteSpace(track))
        {
            return "no upstream";
        }

        var inner = track.Trim('[', ']');

        if (inner.Contains("gone", StringComparison.OrdinalIgnoreCase))
        {
            return "upstream gone";
        }

        // git already words this as "ahead 2, behind 1", which reads well
        // as-is.
        return string.IsNullOrEmpty(inner) ? "up to date" : inner;
    }

    /// <summary>
    /// Switch branches.
    ///
    /// "switch" rather than "checkout": checkout is overloaded to also
    /// discard file changes, and a typo in a branch name should not be able
    /// to silently throw away work.
    /// </summary>
    public Task<GitResult> SwitchBranchAsync(string repoPath, string branch, CancellationToken ct = default) =>
        _git.RunAsync(repoPath, new[] { "switch", branch }, ct);

    public Task<GitResult> CreateBranchAsync(string repoPath, string branch, CancellationToken ct = default) =>
        _git.RunAsync(repoPath, new[] { "switch", "--create", branch }, ct);

    // -----------------------------------------------------------------
    // Clone
    // -----------------------------------------------------------------

    /// <summary>
    /// Clone into a new folder under <paramref name="parentDirectory"/>,
    /// named after the repository.
    /// </summary>
    /// <returns>The path cloned into, or null when the operation failed.</returns>
    public async Task<(GitResult Result, string? Path)> CloneAsync(
        string url,
        string parentDirectory,
        Action<string>? onProgress = null,
        CancellationToken ct = default)
    {
        var name = RepositoryNameFromUrl(url);
        var target = Path.Combine(parentDirectory, name);

        if (Directory.Exists(target) && Directory.EnumerateFileSystemEntries(target).Any())
        {
            return (new GitResult(-1, string.Empty,
                $"A folder named {name} already exists here and is not empty."), null);
        }

        var result = await _git.RunAsync(
            parentDirectory,
            // --progress because git suppresses it when stderr is not a
            // terminal, and without it a large clone reports nothing at all.
            new[] { "clone", "--progress", url, target },
            ct,
            onProgress);

        return (result, result.Success ? target : null);
    }

    /// <summary>
    /// The folder name a clone would use: the last path segment, without a
    /// trailing .git. Handles both https and scp-style ssh URLs.
    /// </summary>
    public static string RepositoryNameFromUrl(string url)
    {
        const string Fallback = "repository";

        var trimmed = url.Trim().TrimEnd('/');
        if (trimmed.Length == 0)
        {
            return Fallback;
        }

        // Strip the scheme first. Without this, the slashes in "https://"
        // are the last ones in a bare host URL and the host itself gets
        // used as the folder name.
        var schemeEnd = trimmed.IndexOf("://", StringComparison.Ordinal);
        if (schemeEnd >= 0)
        {
            trimmed = trimmed[(schemeEnd + 3)..];
        }

        // scp-style ssh: git@host:owner/repo.git
        var colon = trimmed.IndexOf(':');
        if (colon >= 0)
        {
            trimmed = trimmed[(colon + 1)..];
        }

        var segments = trimmed.Split('/', StringSplitOptions.RemoveEmptyEntries);

        // One segment means a host with no path. There is no repository to
        // name, and guessing would clone into a folder named after the host.
        if (segments.Length < 2 && schemeEnd >= 0 && colon < 0)
        {
            return Fallback;
        }

        var name = segments.LastOrDefault() ?? string.Empty;

        if (name.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
        {
            name = name[..^4];
        }

        return string.IsNullOrWhiteSpace(name) ? Fallback : name;
    }

    // -----------------------------------------------------------------
    // Merging and conflicts
    // -----------------------------------------------------------------

    /// <summary>
    /// Is a merge part-way through? Determines whether the UI offers
    /// conflict resolution and an abort.
    /// </summary>
    public async Task<bool> IsMergeInProgressAsync(string repoPath, CancellationToken ct = default)
    {
        var result = await _git.RunAsync(repoPath, new[] { "rev-parse", "--git-dir" }, ct);
        if (!result.Success)
        {
            return false;
        }

        var gitDir = result.StdOut.Trim();
        if (!Path.IsPathRooted(gitDir))
        {
            gitDir = Path.Combine(repoPath, gitDir);
        }

        return File.Exists(Path.Combine(gitDir, "MERGE_HEAD"));
    }

    /// <summary>
    /// Merge the tracked upstream branch. Used when a fast-forward pull was
    /// refused because the branches diverged, and only after the user has
    /// agreed to it.
    /// </summary>
    public Task<GitResult> MergeUpstreamAsync(string repoPath, CancellationToken ct = default) =>
        _git.RunAsync(repoPath, new[] { "merge", "--no-edit" }, ct);

    public Task<GitResult> AbortMergeAsync(string repoPath, CancellationToken ct = default) =>
        _git.RunAsync(repoPath, new[] { "merge", "--abort" }, ct);

    /// <summary>
    /// Resolve a conflict by taking one side wholesale.
    ///
    /// "ours" and "theirs" are famously ambiguous, so the UI never uses those
    /// words: it says which branch each side came from.
    /// </summary>
    public async Task<GitResult> ResolveUsingAsync(
        string repoPath,
        string path,
        ConflictSide side,
        CancellationToken ct = default)
    {
        var flag = side == ConflictSide.Ours ? "--ours" : "--theirs";

        var checkout = await _git.RunAsync(repoPath, new[] { "checkout", flag, "--", path }, ct);
        if (!checkout.Success)
        {
            return checkout;
        }

        // Taking a side is only half the resolution; git still considers the
        // path unmerged until it is staged.
        return await _git.RunAsync(repoPath, new[] { "add", "--", path }, ct);
    }

    /// <summary>
    /// Mark a conflict resolved after the user edited the file themselves.
    /// </summary>
    public Task<GitResult> MarkResolvedAsync(string repoPath, string path, CancellationToken ct = default) =>
        _git.RunAsync(repoPath, new[] { "add", "--", path }, ct);

    /// <summary>
    /// The branch names either side of a conflict, so the UI can say
    /// "keep the version from main" rather than "keep ours".
    /// </summary>
    public async Task<(string Ours, string Theirs)> GetMergeSidesAsync(
        string repoPath,
        CancellationToken ct = default)
    {
        var ours = await _git.RunAsync(repoPath, new[] { "rev-parse", "--abbrev-ref", "HEAD" }, ct);
        var theirs = await _git.RunAsync(repoPath, new[] { "name-rev", "--name-only", "MERGE_HEAD" }, ct);

        return (
            ours.Success && ours.StdOut.Trim().Length > 0 ? ours.StdOut.Trim() : "this branch",
            theirs.Success && theirs.StdOut.Trim().Length > 0 ? theirs.StdOut.Trim() : "the other branch");
    }

    // -----------------------------------------------------------------
    // Diffs
    // -----------------------------------------------------------------

    /// <summary>
    /// The diff for one path.
    /// </summary>
    /// <param name="staged">
    /// True for what is staged (index against HEAD), false for what is not
    /// (working tree against index). The distinction matters: those are
    /// different diffs, and showing one while the user is looking at the
    /// other is how someone commits something they did not mean to.
    /// </param>
    public async Task<FileDiff> GetDiffAsync(
        string repoPath,
        string path,
        bool staged,
        int contextLines = 3,
        CancellationToken ct = default)
    {
        var args = new List<string> { "diff" };

        if (staged)
        {
            args.Add("--cached");
        }

        args.Add($"--unified={contextLines}");

        // No colour and no external tool: we are parsing this, not showing
        // it. A user with diff.external configured would otherwise get
        // something unparseable.
        args.Add("--no-color");
        args.Add("--no-ext-diff");
        args.Add("--");
        args.Add(path);

        var result = await _git.RunRawAsync(repoPath, args, ct);

        if (!result.Success)
        {
            return FileDiff.Empty with { Path = path };
        }

        // An untracked file has nothing to diff against, so git returns
        // nothing at all. Compare it against the empty tree instead, so the
        // user sees its contents as added rather than an empty panel.
        if (string.IsNullOrWhiteSpace(result.StdOut) && !staged)
        {
            var untracked = await _git.RunRawAsync(
                repoPath,
                new[] { "diff", "--no-index", "--no-color", "--no-ext-diff", $"--unified={contextLines}", "/dev/null", path },
                ct);

            // --no-index exits 1 when files differ, which is the normal case
            // here rather than a failure.
            if (!string.IsNullOrWhiteSpace(untracked.StdOut))
            {
                return UnifiedDiffParser.Parse(untracked.StdOut, path);
            }
        }

        return UnifiedDiffParser.Parse(result.StdOut, path);
    }
}

public enum ConflictSide
{
    /// <summary>The version already on the current branch.</summary>
    Ours,

    /// <summary>The version being merged in.</summary>
    Theirs,
}
