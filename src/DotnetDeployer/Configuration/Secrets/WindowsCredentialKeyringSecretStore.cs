using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using CSharpFunctionalExtensions;

namespace DotnetDeployer.Configuration.Secrets;

internal sealed class WindowsCredentialKeyringSecretStore : IKeyringSecretStore
{
    private const uint CredentialTypeGeneric = 1;
    private const uint CredentialPersistLocalMachine = 2;

    public Result Set(string key, string value)
    {
        return Validate(key).Bind(() => Result.Try(() =>
        {
            var bytes = Encoding.Unicode.GetBytes(value);
            var blob = Marshal.AllocHGlobal(bytes.Length);

            try
            {
                Marshal.Copy(bytes, 0, blob, bytes.Length);
                var credential = new NativeCredential
                {
                    Type = CredentialTypeGeneric,
                    TargetName = TargetName(key),
                    CredentialBlob = blob,
                    CredentialBlobSize = (uint)bytes.Length,
                    Persist = CredentialPersistLocalMachine,
                    UserName = Environment.UserName
                };

                if (!CredWrite(ref credential, 0))
                    throw LastWin32Exception("write");
            }
            finally
            {
                Marshal.FreeHGlobal(blob);
            }
        }, ex => ex.Message));
    }

    public Result<string> Get(string key)
    {
        return Validate(key).Bind(() => Result.Try(() =>
        {
            if (!CredRead(TargetName(key), CredentialTypeGeneric, 0, out var credentialPointer))
                throw LastWin32Exception("read");

            try
            {
                var credential = Marshal.PtrToStructure<NativeCredential>(credentialPointer);
                var bytes = new byte[credential.CredentialBlobSize];
                Marshal.Copy(credential.CredentialBlob, bytes, 0, bytes.Length);
                return Encoding.Unicode.GetString(bytes);
            }
            finally
            {
                CredFree(credentialPointer);
            }
        }, ex => ex.Message).Bind(value => ValidateValue(key, value)));
    }

    public Result Delete(string key)
    {
        return Validate(key).Bind(() => Result.Try(() =>
        {
            if (!CredDelete(TargetName(key), CredentialTypeGeneric, 0))
                throw LastWin32Exception("delete");
        }, ex => ex.Message));
    }

    private static Result Validate(string key)
    {
        return string.IsNullOrWhiteSpace(key)
            ? Result.Failure("Secret key is required.")
            : Result.Success();
    }

    private static Result<string> ValidateValue(string key, string value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? Result.Failure<string>($"Secret key '{key}' is empty in system keyring.")
            : Result.Success(value);
    }

    private static string TargetName(string key) => $"DotnetDeployer:{key}";

    private static Win32Exception LastWin32Exception(string operation)
    {
        return new Win32Exception(Marshal.GetLastWin32Error(), $"Failed to {operation} Windows Credential Manager entry.");
    }

    [DllImport("Advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredRead(string target, uint type, uint reservedFlag, out nint credentialPointer);

    [DllImport("Advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredWrite(ref NativeCredential credential, uint flags);

    [DllImport("Advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredDelete(string target, uint type, uint flags);

    [DllImport("Advapi32.dll", EntryPoint = "CredFree", SetLastError = true)]
    private static extern void CredFree(nint credentialPointer);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeCredential
    {
        public uint Flags;
        public uint Type;
        public string TargetName;
        public string? Comment;
        public long LastWritten;
        public uint CredentialBlobSize;
        public nint CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public nint Attributes;
        public string? TargetAlias;
        public string? UserName;
    }
}
