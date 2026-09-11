using GitApp.GitHub;

namespace GitApp.Services;

public static partial class TokenStoreFactory
{
    static partial void CreatePlatform(ref ITokenStore? store) =>
        store = new WindowsCredentialStore();
}
