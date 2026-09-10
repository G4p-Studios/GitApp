using System.Diagnostics;
using System.Text;

namespace GitApp.Services;

/// <summary>
/// The result of one git invocation.
/// </summary>
/// <param name="ExitCode">Zero on success. Git uses 1 for "nothing to do" in
/// several commands, so callers check meaning rather than truthiness.</param>
public sealed record GitResult(int ExitCode, string StdOut, string StdErr)
{
    public bool Success => ExitCode == 0;

    /// <summary>
    /// stderr, trimmed, for showing to a person. Git writes progress to
    /// stderr as well as errors, so this is only meaningful on failure.
    /// </summary>
    public string ErrorMessage => StdErr.Trim();

    /// <summary>Split NUL-delimited output, dropping the trailing empty field.</summary>
    public IReadOnlyList<string> NulFields =>
        StdOut.Split('\0', StringSplitOptions.RemoveEmptyEntries);

    public IReadOnlyList<string> Lines =>
        StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries)
              .Select(l => l.TrimEnd('\r'))
              .ToList();
}

/// <summary>
/// Runs git.exe.
///
/// GitApp shells out rather than binding libgit2. libgit2 does not run hooks,
/// does not support LFS, and does not use the credential helpers the user has
/// already configured, so a commit made through it can behave differently
/// from the same commit made at the terminal. See docs/ARCHITECTURE.md 4.2.
/// </summary>
public sealed class GitProcess
{
    private readonly string _exePath;

    public GitProcess(string exePath = "git")
    {
        _exePath = exePath;
    }

    /// <summary>
    /// Arguments applied to every invocation.
    ///
    /// --no-optional-locks so a background refresh never takes index.lock and
    /// blocks the user's own git at the terminal.
    ///
    /// -c core.quotepath=false so non-ASCII paths come back as real UTF-8
    /// rather than octal escapes, which matters for anyone whose filenames
    /// are not English.
    /// </summary>
    private static readonly string[] GlobalArgs =
    {
        "--no-optional-locks",
        "-c", "core.quotepath=false",
    };

    public async Task<GitResult> RunAsync(
        string workingDirectory,
        IEnumerable<string> args,
        CancellationToken cancellationToken = default)
    {
        var info = new ProcessStartInfo
        {
            FileName = _exePath,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        foreach (var a in GlobalArgs)
        {
            info.ArgumentList.Add(a);
        }

        foreach (var a in args)
        {
            info.ArgumentList.Add(a);
        }

        // Without this, git can sit waiting on a credential prompt that has
        // nowhere to appear. The process hangs, the UI shows nothing, and a
        // screen reader user has no way to tell the difference between slow
        // and stuck. Fail instead, so we can announce the failure.
        info.Environment["GIT_TERMINAL_PROMPT"] = "0";

        // Keep output stable regardless of the user's locale. Parsing
        // localized git output is a bug waiting for a German tester.
        info.Environment["LC_ALL"] = "C";

        using var process = new Process { StartInfo = info };

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                stdout.Append(e.Data).Append('\n');
            }
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                stderr.Append(e.Data).Append('\n');
            }
        };

        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            // Almost always git not being on PATH. Say so plainly rather than
            // surfacing a Win32 error code.
            return new GitResult(-1, string.Empty,
                $"Could not start git. Is it installed and on your PATH? ({ex.Message})");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }

        return new GitResult(process.ExitCode, stdout.ToString(), stderr.ToString());
    }

    /// <summary>
    /// Run a command whose output is NUL-delimited. The line-based reader
    /// above would mangle it, so this reads stdout as one string.
    /// </summary>
    public async Task<GitResult> RunRawAsync(
        string workingDirectory,
        IEnumerable<string> args,
        CancellationToken cancellationToken = default)
    {
        var info = new ProcessStartInfo
        {
            FileName = _exePath,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        foreach (var a in GlobalArgs)
        {
            info.ArgumentList.Add(a);
        }

        foreach (var a in args)
        {
            info.ArgumentList.Add(a);
        }

        info.Environment["GIT_TERMINAL_PROMPT"] = "0";
        info.Environment["LC_ALL"] = "C";

        using var process = new Process { StartInfo = info };

        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            return new GitResult(-1, string.Empty,
                $"Could not start git. Is it installed and on your PATH? ({ex.Message})");
        }

        // Read both streams concurrently. Reading one to completion first can
        // deadlock when the other fills its pipe buffer.
        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }

        return new GitResult(process.ExitCode, await stdoutTask, await stderrTask);
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Already gone. Nothing useful to do.
        }
    }

    /// <summary>The installed git version, or null when git cannot be run.</summary>
    public async Task<string?> TryGetVersionAsync(CancellationToken cancellationToken = default)
    {
        var result = await RunAsync(Environment.CurrentDirectory, new[] { "--version" }, cancellationToken);
        return result.Success ? result.StdOut.Trim() : null;
    }
}
