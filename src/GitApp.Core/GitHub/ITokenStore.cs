namespace GitApp.GitHub;

/// <summary>
/// Where the access token lives.
///
/// An interface because the core library must not depend on a UI framework,
/// and because the real implementation writes to the platform credential
/// store, which is not something a unit test should touch.
///
/// The contract, which matters more than the shape: a token never lands in
/// a file this app writes, never appears in a log line, and never goes into
/// an error message. Anything that formats an exception for display has to
/// assume the token could be in it.
/// </summary>
public interface ITokenStore
{
    Task<string?> GetAsync(string key);

    Task SetAsync(string key, string token);

    Task RemoveAsync(string key);
}

/// <summary>
/// For tests, and for a first run before anything is stored. Deliberately
/// not a fallback the app silently uses: a token that survives only until
/// the process exits would make sign-in look broken rather than absent.
/// </summary>
public sealed class InMemoryTokenStore : ITokenStore
{
    private readonly Dictionary<string, string> _tokens = new();

    public Task<string?> GetAsync(string key) =>
        Task.FromResult(_tokens.TryGetValue(key, out var token) ? token : null);

    public Task SetAsync(string key, string token)
    {
        _tokens[key] = token;
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string key)
    {
        _tokens.Remove(key);
        return Task.CompletedTask;
    }
}
