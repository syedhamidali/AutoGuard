using System.Runtime.InteropServices;
using System.Text;

namespace AutoPortal.Connectivity.Core;

public interface ICredentialStore
{
    (string User, string Password)? Load();
    void Save(string user, string password);
    /// <summary>Removes the stored credential. Only called on explicit user request.</summary>
    void Clear();
}

/// <summary>
/// Stores the login in Windows Credential Manager (encrypted by Windows for the current user).
/// The password is never written to settings files or logs.
/// </summary>
public sealed class WindowsCredentialStore : ICredentialStore
{
    const string Target = "AutoPortal/Connectivity/Portal";
    const uint TypeGeneric = 1, PersistLocalMachine = 2;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct CREDENTIAL
    {
        public uint Flags, Type;
        public string TargetName, Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist, AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias, UserName;
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool CredWrite(ref CREDENTIAL c, uint flags);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool CredRead(string target, uint type, uint flags, out IntPtr c);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool CredDelete(string target, uint type, uint flags);
    [DllImport("advapi32.dll")] static extern void CredFree(IntPtr c);

    public void Save(string user, string password)
    {
        var bytes = Encoding.Unicode.GetBytes(password);
        var blob = Marshal.AllocHGlobal(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, blob, bytes.Length);
            var c = new CREDENTIAL
            {
                Type = TypeGeneric, TargetName = Target, UserName = user, CredentialBlob = blob,
                CredentialBlobSize = (uint)bytes.Length, Persist = PersistLocalMachine, Comment = "AutoPortal Connectivity Manager"
            };
            if (!CredWrite(ref c, 0)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        }
        finally { Array.Clear(bytes); Marshal.FreeHGlobal(blob); }
    }

    public (string User, string Password)? Load()
    {
        if (!CredRead(Target, TypeGeneric, 0, out var p)) return null;
        try
        {
            var c = Marshal.PtrToStructure<CREDENTIAL>(p);
            var pw = c.CredentialBlobSize == 0 ? "" : Marshal.PtrToStringUni(c.CredentialBlob, (int)c.CredentialBlobSize / 2) ?? "";
            return (c.UserName ?? "", pw);
        }
        finally { CredFree(p); }
    }

    public void Clear() => CredDelete(Target, TypeGeneric, 0);
}

public sealed class InMemoryCredentialStore : ICredentialStore
{
    (string, string)? _c;
    public (string User, string Password)? Load() => _c;
    public void Save(string user, string password) => _c = (user, password);
    public void Clear() => _c = null;
}
