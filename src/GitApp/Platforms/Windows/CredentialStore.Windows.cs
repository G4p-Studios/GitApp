using System.Runtime.InteropServices;
using System.Text;
using GitApp.GitHub;

namespace GitApp.Services;

/// <summary>
/// Tokens in the Windows Credential Manager.
///
/// Not MAUI's SecureStorage, which on Windows goes through
/// ApplicationData.Current and therefore needs package identity. GitApp is
/// unpackaged (WindowsPackageType None), so that call throws, and a token
/// store that throws on a first run is a sign-in screen that never works.
/// The Win32 API underneath has no such requirement.
///
/// Credentials appear under Control Panel, Credential Manager, Windows
/// Credentials as "GitApp: github.com", which means the user can see and
/// revoke what the app holds without the app's help. That is deliberate:
/// a credential only this app can find is a credential the user cannot
/// audit.
/// </summary>
public sealed class WindowsCredentialStore : ITokenStore
{
    private const int CRED_TYPE_GENERIC = 1;
    private const int CRED_PERSIST_LOCAL_MACHINE = 2;
    private const int ERROR_NOT_FOUND = 1168;

    private static string TargetName(string key) => $"GitApp: {key}";

    public Task<string?> GetAsync(string key)
    {
        if (!CredRead(TargetName(key), CRED_TYPE_GENERIC, 0, out var handle))
        {
            var error = Marshal.GetLastWin32Error();

            // Nothing stored is the normal first-run state, not a failure.
            return Task.FromResult<string?>(
                error == ERROR_NOT_FOUND ? null : null);
        }

        try
        {
            var credential = Marshal.PtrToStructure<CREDENTIAL>(handle);

            if (credential.CredentialBlobSize == 0 || credential.CredentialBlob == IntPtr.Zero)
            {
                return Task.FromResult<string?>(null);
            }

            var bytes = new byte[credential.CredentialBlobSize];
            Marshal.Copy(credential.CredentialBlob, bytes, 0, bytes.Length);

            return Task.FromResult<string?>(Encoding.Unicode.GetString(bytes));
        }
        finally
        {
            CredFree(handle);
        }
    }

    public Task SetAsync(string key, string token)
    {
        var blob = Encoding.Unicode.GetBytes(token);
        var blobHandle = Marshal.AllocHGlobal(blob.Length);

        try
        {
            Marshal.Copy(blob, 0, blobHandle, blob.Length);

            var credential = new CREDENTIAL
            {
                Type = CRED_TYPE_GENERIC,
                TargetName = TargetName(key),
                CredentialBlobSize = blob.Length,
                CredentialBlob = blobHandle,
                Persist = CRED_PERSIST_LOCAL_MACHINE,
                UserName = key,
            };

            if (!CredWrite(ref credential, 0))
            {
                // Deliberately not surfacing the Win32 error text: it is
                // meaningless to the user, and anything formatted from a
                // credential call is somewhere a token could leak.
                throw new InvalidOperationException(
                    "Windows would not save the sign-in. You will have to sign in again next time.");
            }
        }
        finally
        {
            // Zero the copy before releasing it, so the token does not sit
            // in freed memory waiting to be reused by something else.
            for (var i = 0; i < blob.Length; i++)
            {
                Marshal.WriteByte(blobHandle, i, 0);
            }

            Marshal.FreeHGlobal(blobHandle);
            Array.Clear(blob);
        }

        return Task.CompletedTask;
    }

    public Task RemoveAsync(string key)
    {
        // A missing credential is already the desired state.
        CredDelete(TargetName(key), CRED_TYPE_GENERIC, 0);
        return Task.CompletedTask;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CREDENTIAL
    {
        public int Flags;
        public int Type;
        [MarshalAs(UnmanagedType.LPWStr)] public string TargetName;
        [MarshalAs(UnmanagedType.LPWStr)] public string? Comment;
        public long LastWritten;
        public int CredentialBlobSize;
        public IntPtr CredentialBlob;
        public int Persist;
        public int AttributeCount;
        public IntPtr Attributes;
        [MarshalAs(UnmanagedType.LPWStr)] public string? TargetAlias;
        [MarshalAs(UnmanagedType.LPWStr)] public string? UserName;
    }

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredRead(string target, int type, int reservedFlag, out IntPtr credential);

    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredWrite(ref CREDENTIAL credential, int flags);

    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredDelete(string target, int type, int flags);

    [DllImport("advapi32.dll", EntryPoint = "CredFree")]
    private static extern void CredFree(IntPtr buffer);
}
