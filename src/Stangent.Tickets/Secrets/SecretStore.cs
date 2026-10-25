using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Stangent.Core;

namespace Stangent.Tickets.Secrets;

/// <summary>
/// Secrets (Jira refresh tokens, API tokens) kept in the OS keychain, never in a file (D73).
/// One secret per (service, account). Values are UTF-8 strings.
/// </summary>
public interface ISecretStore
{
    string? Get(string service, string account);
    void Set(string service, string account, string value);
    /// <summary>Returns whether something was removed.</summary>
    bool Remove(string service, string account);
}

/// <summary>For tests: secrets in memory.</summary>
public sealed class MemorySecretStore : ISecretStore
{
    readonly Dictionary<(string, string), string> items = [];
    public string? Get(string service, string account) => items.GetValueOrDefault((service, account));
    public void Set(string service, string account, string value) => items[(service, account)] = value;
    public bool Remove(string service, string account) => items.Remove((service, account));
}

public static class SecretStores
{
    /// <summary>The OS keychain: macOS Keychain, Windows Credential Manager, or the Secret Service on Linux.</summary>
    public static ISecretStore Os() =>
        OperatingSystem.IsMacOS() ? new MacKeychain()
        : OperatingSystem.IsWindows() ? new WindowsCredentialStore()
        : new LinuxSecretTool();
}

/// <summary>
/// macOS Keychain through the Security framework's generic-password API, called in-process, so a secret never appears
/// in a process's arguments, a pipe or an error message. (The <c>security</c> tool was tried first: its interactive
/// mode rejects long lines and echoes them back in its error, which printed a token.)
/// </summary>
sealed class MacKeychain : ISecretStore
{
    const string Security = "/System/Library/Frameworks/Security.framework/Security";
    const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    const int ItemNotFound = -25300;

    public string? Get(string service, string account)
    {
        var (s, a) = (Encoding.UTF8.GetBytes(service), Encoding.UTF8.GetBytes(account));
        var status = SecKeychainFindGenericPassword(IntPtr.Zero, (uint)s.Length, s, (uint)a.Length, a, out var length, out var data, out var item);
        if (status == ItemNotFound) return null;
        if (status != 0) throw Failed("read", status);
        try
        {
            var bytes = new byte[length];
            Marshal.Copy(data, bytes, 0, (int)length);
            return Encoding.UTF8.GetString(bytes);
        }
        finally
        {
            SecKeychainItemFreeContent(IntPtr.Zero, data);
            if (item != IntPtr.Zero) CFRelease(item);
        }
    }

    public void Set(string service, string account, string value)
    {
        var (s, a, v) = (Encoding.UTF8.GetBytes(service), Encoding.UTF8.GetBytes(account), Encoding.UTF8.GetBytes(value));
        var status = SecKeychainFindGenericPassword(IntPtr.Zero, (uint)s.Length, s, (uint)a.Length, a, out _, IntPtr.Zero, out var item);
        if (status == 0)
        {
            try { status = SecKeychainItemModifyAttributesAndData(item, IntPtr.Zero, (uint)v.Length, v); }
            finally { CFRelease(item); }
        }
        else if (status == ItemNotFound)
        {
            status = SecKeychainAddGenericPassword(IntPtr.Zero, (uint)s.Length, s, (uint)a.Length, a, (uint)v.Length, v, IntPtr.Zero);
        }
        if (status != 0) throw Failed("save", status);
    }

    public bool Remove(string service, string account)
    {
        var (s, a) = (Encoding.UTF8.GetBytes(service), Encoding.UTF8.GetBytes(account));
        if (SecKeychainFindGenericPassword(IntPtr.Zero, (uint)s.Length, s, (uint)a.Length, a, out _, IntPtr.Zero, out var item) != 0) return false;
        try { return SecKeychainItemDelete(item) == 0; }
        finally { CFRelease(item); }
    }

    // The message carries only the OSStatus code, never the secret.
    static StangentException Failed(string what, int status) =>
        new("keychain_failed", $"Couldn't {what} the macOS Keychain item (OSStatus {status}).");

    [DllImport(Security)] static extern int SecKeychainFindGenericPassword(IntPtr keychain, uint serviceLength, byte[] service, uint accountLength, byte[] account, out uint passwordLength, out IntPtr passwordData, out IntPtr item);
    [DllImport(Security)] static extern int SecKeychainFindGenericPassword(IntPtr keychain, uint serviceLength, byte[] service, uint accountLength, byte[] account, out uint passwordLength, IntPtr passwordData, out IntPtr item);
    [DllImport(Security)] static extern int SecKeychainAddGenericPassword(IntPtr keychain, uint serviceLength, byte[] service, uint accountLength, byte[] account, uint passwordLength, byte[] password, IntPtr item);
    [DllImport(Security)] static extern int SecKeychainItemModifyAttributesAndData(IntPtr item, IntPtr attributes, uint length, byte[] data);
    [DllImport(Security)] static extern int SecKeychainItemDelete(IntPtr item);
    [DllImport(Security)] static extern int SecKeychainItemFreeContent(IntPtr attributes, IntPtr data);
    [DllImport(CoreFoundation)] static extern void CFRelease(IntPtr cf);
}

/// <summary>Linux Secret Service through <c>secret-tool</c>; the secret goes on stdin.</summary>
sealed class LinuxSecretTool : ISecretStore
{
    public string? Get(string service, string account)
    {
        var (code, stdout, _) = ProcessRunner.Run("secret-tool", ["lookup", "service", service, "account", account], null);
        return code == 0 && stdout.Length > 0 ? stdout : null;
    }

    public void Set(string service, string account, string value)
    {
        var (code, _, _) = ProcessRunner.Run("secret-tool", ["store", "--label", $"{service} ({account})", "service", service, "account", account], value);
        if (code != 0) throw new StangentException("keychain_failed", $"Couldn't save with secret-tool (exit {code}).");
    }

    public bool Remove(string service, string account) =>
        ProcessRunner.Run("secret-tool", ["clear", "service", service, "account", account], null).Code == 0;
}

/// <summary>Windows Credential Manager (generic credentials).</summary>
sealed class WindowsCredentialStore : ISecretStore
{
    static string Target(string service, string account) => $"{service}:{account}";

    public string? Get(string service, string account)
    {
        if (!CredReadW(Target(service, account), 1, 0, out var ptr)) return null;
        try
        {
            var cred = Marshal.PtrToStructure<Credential>(ptr);
            return cred.CredentialBlobSize == 0 ? "" : Marshal.PtrToStringUni(cred.CredentialBlob, (int)cred.CredentialBlobSize / 2);
        }
        finally { CredFree(ptr); }
    }

    public void Set(string service, string account, string value)
    {
        var blob = Marshal.StringToCoTaskMemUni(value);
        try
        {
            var cred = new Credential
            {
                Type = 1, TargetName = Target(service, account), UserName = account, Persist = 2,
                CredentialBlob = blob, CredentialBlobSize = (uint)(value.Length * 2),
            };
            if (!CredWriteW(ref cred, 0))
                throw new StangentException("keychain_failed", $"Couldn't save to Windows Credential Manager (error {Marshal.GetLastWin32Error()}).");
        }
        finally { Marshal.FreeCoTaskMem(blob); }
    }

    public bool Remove(string service, string account) => CredDeleteW(Target(service, account), 1, 0);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct Credential
    {
        public uint Flags, Type;
        public string TargetName;
        public string? Comment;
        public long LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist, AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias;
        public string UserName;
    }

    [DllImport("advapi32", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool CredReadW(string target, uint type, uint flags, out IntPtr credential);
    [DllImport("advapi32", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool CredWriteW(ref Credential credential, uint flags);
    [DllImport("advapi32", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool CredDeleteW(string target, uint type, uint flags);
    [DllImport("advapi32")] static extern void CredFree(IntPtr buffer);
}

static class ProcessRunner
{
    public static (int Code, string Stdout, string Stderr) Run(string file, IEnumerable<string> args, string? stdin)
    {
        var psi = new ProcessStartInfo(file) { RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = stdin is not null };
        foreach (var a in args) psi.ArgumentList.Add(a);
        Process p;
        try { p = Process.Start(psi)!; }
        catch (System.ComponentModel.Win32Exception e)
        {
            throw new StangentException("keychain_unavailable", $"'{file}' isn't available: {e.Message}");
        }
        using (p)
        {
            if (stdin is not null) { p.StandardInput.Write(stdin); p.StandardInput.Close(); }
            var outTask = p.StandardOutput.ReadToEndAsync();
            var err = p.StandardError.ReadToEnd();
            p.WaitForExit();
            return (p.ExitCode, outTask.Result, err);
        }
    }
}
