namespace GitApp.GitHub;

/// <summary>
/// The outcome of a GitHub call, with a message written for a person rather
/// than a log.
///
/// Every failure here gets said out loud, so "Unauthorized" and
/// "HTTP 403" are not acceptable messages. What the user needs is what went
/// wrong and what to do about it, in one sentence they can act on.
/// </summary>
public sealed record GitHubResult<T>(T? Value, string? Error, GitHubFailure Failure = GitHubFailure.None)
{
    public bool Success => Error is null;

    public static GitHubResult<T> Ok(T value) => new(value, null);

    public static GitHubResult<T> Fail(string message, GitHubFailure kind = GitHubFailure.Other) =>
        new(default, message, kind);
}

/// <summary>
/// Why a call failed, where the caller needs to behave differently rather
/// than just report it. Signing out on a revoked token, for instance.
/// </summary>
public enum GitHubFailure
{
    None,

    /// <summary>The token is missing, expired or revoked. Sign in again.</summary>
    Unauthenticated,

    /// <summary>Authenticated, but not allowed. A scope is usually missing.</summary>
    Forbidden,

    /// <summary>Rate limited. The message says when it resets.</summary>
    RateLimited,

    /// <summary>No network, DNS failure, timeout.</summary>
    Offline,

    NotFound,

    Other,
}
