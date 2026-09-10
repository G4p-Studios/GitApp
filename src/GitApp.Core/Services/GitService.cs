using GitApp.Domain;

namespace GitApp.Services;

/// <summary>
/// Git operations, in terms the app understands.
///
/// Everything here parses <c>--porcelain=v2 -z</c> rather than human-readable
/// output. Git's normal output is localized and reformatted between versions;
/// the porcelain formats are contracts. See docs/ARCHITECTURE.md 4.2.
/// </summary>
public sealed class GitService
{
    private readonly GitProcess _git;

    public GitService(GitProcess? git = null)
    {
        _git = git ?? new GitProcess();
    }

    /// <summary>
    /// The repository root containing <paramref name="path"/>, or null when
    /// it is not inside a work tree. Resolving to the root means the user can
    /// add any subdirectory and get the repository they meant.
    /// </summary>
    public async Task<string?> FindRepositoryRootAsync(string path, CancellationToken ct = default)
    {
        if (!Directory.Exists(path))
        {
            return null;
        }

        var result = await _git.RunAsync(path, new[] { "rev-parse", "--show-toplevel" }, ct);
        if (!result.Success)
        {
            return null;
        }

        var root = result.StdOut.Trim();
        return string.IsNullOrEmpty(root) ? null : Path.GetFullPath(root);
    }

    /// <summary>
    /// Working tree state: branch, upstream, ahead/behind, and every change.
    /// </summary>
    public async Task<RepoStatus> GetStatusAsync(string repoPath, CancellationToken ct = default)
    {
        var result = await _git.RunRawAsync(
            repoPath,
            new[] { "status", "--porcelain=v2", "--branch", "--untracked-files=all", "-z" },
            ct);

        if (!result.Success)
        {
            return RepoStatus.Empty;
        }

        return ParseStatus(result.StdOut);
    }

    /// <summary>
    /// Parse <c>git status --porcelain=v2 --branch -z</c>.
    ///
    /// Records are NUL-terminated. Rename and copy entries (type 2) are the
    /// awkward case: their path field is itself NUL-separated from the
    /// original path, so one logical record spans two fields. Splitting
    /// naively on NUL puts the original path where the next record should be
    /// and every subsequent row shifts by one.
    /// </summary>
    internal static RepoStatus ParseStatus(string raw)
    {
        var fields = raw.Split('\0');

        var branch = "unknown";
        string? upstream = null;
        var ahead = 0;
        var behind = 0;
        var detached = false;
        var changes = new List<FileChange>();

        for (var i = 0; i < fields.Length; i++)
        {
            var field = fields[i];
            if (string.IsNullOrEmpty(field))
            {
                continue;
            }

            switch (field[0])
            {
                case '#':
                    ReadHeader(field, ref branch, ref upstream, ref ahead, ref behind, ref detached);
                    break;

                case '1':
                    if (ParseOrdinary(field) is { } ordinary)
                    {
                        changes.Add(ordinary);
                    }

                    break;

                case '2':
                    // Consumes the following field as the original path.
                    var original = i + 1 < fields.Length ? fields[++i] : null;
                    if (ParseRenamed(field, original) is { } renamed)
                    {
                        changes.Add(renamed);
                    }

                    break;

                case 'u':
                    if (ParseUnmerged(field) is { } unmerged)
                    {
                        changes.Add(unmerged);
                    }

                    break;

                case '?':
                    changes.Add(new FileChange(
                        field[2..],
                        ChangeKind.Unmodified,
                        ChangeKind.Untracked));
                    break;

                // '!' is ignored files, which we do not request.
            }
        }

        changes.Sort((a, b) => string.Compare(a.Path, b.Path, StringComparison.OrdinalIgnoreCase));

        return new RepoStatus(branch, upstream, ahead, behind, changes, detached);
    }

    private static void ReadHeader(
        string field,
        ref string branch,
        ref string? upstream,
        ref int ahead,
        ref int behind,
        ref bool detached)
    {
        // "# branch.head main", "# branch.ab +1 -2"
        var parts = field.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3)
        {
            return;
        }

        switch (parts[1])
        {
            case "branch.head":
                branch = parts[2];
                // Git writes the literal "(detached)" here, not a branch name.
                detached = branch == "(detached)";
                break;

            case "branch.upstream":
                upstream = parts[2];
                break;

            case "branch.ab":
                if (parts.Length >= 4)
                {
                    ahead = ParseSigned(parts[2]);
                    behind = ParseSigned(parts[3]);
                }

                break;
        }
    }

    private static int ParseSigned(string token) =>
        int.TryParse(token.AsSpan(1), out var value) ? Math.Abs(value) : 0;

    /// <summary>
    /// Ordinary change: <c>1 XY sub mH mI mW hH hI path</c>
    /// </summary>
    private static FileChange? ParseOrdinary(string field)
    {
        var parts = field.Split(' ', 9);
        if (parts.Length < 9)
        {
            return null;
        }

        var xy = parts[1];
        return new FileChange(parts[8], FromCode(xy[0]), FromCode(xy[1]));
    }

    /// <summary>
    /// Rename or copy: <c>2 XY sub mH mI mW hH hI Xscore path</c>, with the
    /// original path following as a separate NUL-delimited field.
    /// </summary>
    private static FileChange? ParseRenamed(string field, string? originalPath)
    {
        var parts = field.Split(' ', 10);
        if (parts.Length < 10)
        {
            return null;
        }

        var xy = parts[1];
        return new FileChange(parts[9], FromCode(xy[0]), FromCode(xy[1]), originalPath);
    }

    /// <summary>
    /// Unmerged: <c>u XY sub m1 m2 m3 mW h1 h2 h3 path</c>
    /// </summary>
    private static FileChange? ParseUnmerged(string field)
    {
        var parts = field.Split(' ', 11);
        if (parts.Length < 11)
        {
            return null;
        }

        return new FileChange(parts[10], ChangeKind.Conflicted, ChangeKind.Conflicted);
    }

    private static ChangeKind FromCode(char code) => code switch
    {
        'M' => ChangeKind.Modified,
        'A' => ChangeKind.Added,
        'D' => ChangeKind.Deleted,
        'R' => ChangeKind.Renamed,
        'C' => ChangeKind.Copied,
        'U' => ChangeKind.Conflicted,
        '?' => ChangeKind.Untracked,
        '!' => ChangeKind.Ignored,
        _ => ChangeKind.Unmodified,
    };

    // -----------------------------------------------------------------
    // Operations
    // -----------------------------------------------------------------

    public Task<GitResult> StageAsync(string repoPath, IEnumerable<string> paths, CancellationToken ct = default)
    {
        // "--" so a path that looks like an option is still treated as a path.
        var args = new List<string> { "add", "--" };
        args.AddRange(paths);
        return _git.RunAsync(repoPath, args, ct);
    }

    public Task<GitResult> StageAllAsync(string repoPath, CancellationToken ct = default) =>
        _git.RunAsync(repoPath, new[] { "add", "--all" }, ct);

    public Task<GitResult> UnstageAsync(string repoPath, IEnumerable<string> paths, CancellationToken ct = default)
    {
        var args = new List<string> { "restore", "--staged", "--" };
        args.AddRange(paths);
        return _git.RunAsync(repoPath, args, ct);
    }

    public Task<GitResult> CommitAsync(string repoPath, string message, CancellationToken ct = default) =>
        // Message passed as an argument, never through a shell, so quoting
        // and newlines in it cannot be reinterpreted.
        _git.RunAsync(repoPath, new[] { "commit", "-m", message }, ct);

    public Task<GitResult> FetchAsync(string repoPath, CancellationToken ct = default) =>
        _git.RunAsync(repoPath, new[] { "fetch", "--prune" }, ct);

    public Task<GitResult> PullAsync(string repoPath, CancellationToken ct = default) =>
        // --ff-only rather than a merge or rebase. Anything else can leave the
        // user mid-conflict without having asked for it, and conflict
        // resolution is not built yet.
        _git.RunAsync(repoPath, new[] { "pull", "--ff-only" }, ct);

    public Task<GitResult> PushAsync(string repoPath, CancellationToken ct = default) =>
        _git.RunAsync(repoPath, new[] { "push" }, ct);

    /// <summary>Push and set upstream, for a branch that has none.</summary>
    public async Task<GitResult> PushSetUpstreamAsync(string repoPath, string branch, CancellationToken ct = default)
    {
        var remote = await GetDefaultRemoteAsync(repoPath, ct) ?? "origin";
        return await _git.RunAsync(repoPath, new[] { "push", "--set-upstream", remote, branch }, ct);
    }

    public async Task<string?> GetDefaultRemoteAsync(string repoPath, CancellationToken ct = default)
    {
        var result = await _git.RunAsync(repoPath, new[] { "remote" }, ct);
        if (!result.Success)
        {
            return null;
        }

        var remotes = result.Lines;
        return remotes.Contains("origin") ? "origin" : remotes.FirstOrDefault();
    }

    public async Task<IReadOnlyList<string>> GetBranchesAsync(string repoPath, CancellationToken ct = default)
    {
        var result = await _git.RunAsync(
            repoPath,
            new[] { "for-each-ref", "--format=%(refname:short)", "refs/heads" },
            ct);

        return result.Success ? result.Lines : Array.Empty<string>();
    }

    public Task<GitResult> CheckoutAsync(string repoPath, string branch, CancellationToken ct = default) =>
        _git.RunAsync(repoPath, new[] { "checkout", branch }, ct);

    public Task<GitResult> CloneAsync(string url, string targetDirectory, CancellationToken ct = default) =>
        _git.RunAsync(
            Path.GetDirectoryName(targetDirectory) ?? Environment.CurrentDirectory,
            new[] { "clone", url, targetDirectory },
            ct);

    /// <summary>Recent commits on the current branch, newest first.</summary>
    public async Task<IReadOnlyList<CommitInfo>> GetLogAsync(
        string repoPath,
        int count = 50,
        CancellationToken ct = default)
    {
        // ASCII unit and record separators. Neither can occur in a commit
        // message, so no escaping or quoting is needed and a subject
        // containing newlines, tabs or quotes still parses correctly.
        // Written as escapes rather than literal control bytes so the format
        // string survives an editor, a diff and a copy-paste.
        const char UnitSeparator = '\u001f';
        const char RecordSeparator = '\u001e';

        var format = $"--format=%h{UnitSeparator}%an{UnitSeparator}%ar{UnitSeparator}%s{RecordSeparator}";

        var result = await _git.RunRawAsync(
            repoPath,
            new[] { "log", $"--max-count={count}", format },
            ct);

        if (!result.Success)
        {
            return Array.Empty<CommitInfo>();
        }

        return result.StdOut
            .Split(RecordSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(record => record.Trim('\n', '\r'))
            .Where(record => record.Length > 0)
            .Select(record => record.Split(UnitSeparator))
            .Where(parts => parts.Length >= 4)
            .Select(parts => new CommitInfo(parts[0], parts[1], parts[2], parts[3]))
            .ToList();
    }
}
