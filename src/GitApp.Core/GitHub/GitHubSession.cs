using GitApp.Domain;

namespace GitApp.GitHub;

/// <summary>
/// Signed in, or not. One place that owns the token so nothing else has to
/// handle it.
///
/// The client ID for browser sign-in is supplied rather than compiled in:
/// GitApp has no registered OAuth application yet, and inventing one by
/// borrowing another product's client ID would mean users granting access
/// to something that is not this app. Until it is registered, personal
/// access tokens are the whole story, and the app says so plainly instead
/// of offering a button that cannot work.
/// </summary>
public sealed class GitHubSession
{
    private const string TokenKey = "github.com";

    private readonly ITokenStore _tokens;
    private readonly Func<string, HttpMessageHandler?, GitHubClient> _clientFactory;

    private string? _token;

    public GitHubSession(
        ITokenStore? tokens = null,
        Func<string, HttpMessageHandler?, GitHubClient>? clientFactory = null,
        string? deviceFlowClientId = null)
    {
        _tokens = tokens ?? new InMemoryTokenStore();
        _clientFactory = clientFactory ?? ((token, handler) => new GitHubClient(token, handler));
        DeviceFlowClientId = deviceFlowClientId;
    }

    /// <summary>Null when browser sign-in is not available in this build.</summary>
    public string? DeviceFlowClientId { get; }

    public bool CanUseBrowserSignIn => !string.IsNullOrWhiteSpace(DeviceFlowClientId);

    public GitHubAccount? Account { get; private set; }

    public bool IsSignedIn => Account is not null;

    /// <summary>
    /// Spoken on sign-in and shown in the pane heading. Signed out is a
    /// state that has to be said, not implied by an empty list.
    /// </summary>
    public string StatusDescription => Account is { } account
        ? account.AccessibleName
        : "Not signed in to GitHub";

    /// <summary>
    /// Restore a previous session, if the token is still good.
    ///
    /// A revoked token is cleared rather than kept, so the app does not
    /// spend the rest of the session failing every call with an error the
    /// user cannot act on.
    /// </summary>
    public async Task<GitHubResult<GitHubAccount>> RestoreAsync(CancellationToken ct = default)
    {
        var stored = await _tokens.GetAsync(TokenKey);
        if (string.IsNullOrWhiteSpace(stored))
        {
            return GitHubResult<GitHubAccount>.Fail("Not signed in to GitHub.",
                GitHubFailure.Unauthenticated);
        }

        return await AdoptAsync(stored, persist: false, ct);
    }

    /// <summary>Sign in with a personal access token the user pasted.</summary>
    public Task<GitHubResult<GitHubAccount>> SignInWithTokenAsync(
        string token, CancellationToken ct = default)
    {
        var trimmed = token?.Trim();

        return string.IsNullOrEmpty(trimmed)
            ? Task.FromResult(GitHubResult<GitHubAccount>.Fail("Enter a token to sign in."))
            : AdoptAsync(trimmed, persist: true, ct);
    }

    public async Task SignOutAsync()
    {
        _token = null;
        Account = null;
        await _tokens.RemoveAsync(TokenKey);
    }

    /// <summary>
    /// A client for the current token. Null when signed out, so callers
    /// have to deal with it rather than discovering a 401 later.
    /// </summary>
    public GitHubClient? CreateClient(HttpMessageHandler? handler = null) =>
        _token is null ? null : _clientFactory(_token, handler);

    private async Task<GitHubResult<GitHubAccount>> AdoptAsync(
        string token, bool persist, CancellationToken ct)
    {
        using var client = _clientFactory(token, null);
        var who = await client.GetAccountAsync(ct);

        if (!who.Success)
        {
            // Only clear a stored token when GitHub actively rejected it.
            // Being offline is not a reason to make the user sign in again.
            if (who.Failure == GitHubFailure.Unauthenticated)
            {
                await _tokens.RemoveAsync(TokenKey);
            }

            return who;
        }

        _token = token;
        Account = who.Value;

        if (persist)
        {
            await _tokens.SetAsync(TokenKey, token);
        }

        return who;
    }
}
