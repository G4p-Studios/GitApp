using GitApp.GitHub;

namespace GitApp.Services;

/// <summary>
/// Picks the right credential store for the platform.
///
/// Windows gets the Credential Manager directly, because GitApp is
/// unpackaged and MAUI's SecureStorage needs package identity there.
/// Everywhere else SecureStorage is correct and already wraps the platform
/// keychain, so there is nothing to add.
/// </summary>
public static partial class TokenStoreFactory
{
    public static ITokenStore Create()
    {
        ITokenStore? store = null;
        CreatePlatform(ref store);

        return store ?? new SecureStorageTokenStore();
    }

    static partial void CreatePlatform(ref ITokenStore? store);
}

/// <summary>
/// The cross-platform default. Keychain on macOS.
/// </summary>
public sealed class SecureStorageTokenStore : ITokenStore
{
    public Task<string?> GetAsync(string key) => SecureStorage.Default.GetAsync(Key(key));

    public Task SetAsync(string key, string token) => SecureStorage.Default.SetAsync(Key(key), token);

    public Task RemoveAsync(string key)
    {
        SecureStorage.Default.Remove(Key(key));
        return Task.CompletedTask;
    }

    private static string Key(string key) => $"gitapp.token.{key}";
}
