using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace LuKnight.Services;

public interface ICredentialService
{
    string? Read();
    void Write(string key);
    void Remove();
}

public sealed class SecureCredentialService : ICredentialService
{
    public const string Target = "LuKnight/GeminiApiKey";
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Credential
    {
        public uint Flags, Type;
        public string TargetName;
        public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public nint CredentialBlob;
        public uint Persist, AttributeCount;
        public nint Attributes;
        public string? TargetAlias, UserName;
    }
    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredRead(string target, uint type, uint flags, out nint credential);
    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredWrite(ref Credential credential, uint flags);
    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredDelete(string target, uint type, uint flags);
    [DllImport("advapi32.dll")] private static extern void CredFree(nint credential);
    public string? Read()
    {
        if (!CredRead(Target, 1, 0, out var pointer))
        { if (Marshal.GetLastWin32Error() == 1168) return null; throw new Win32Exception(Marshal.GetLastWin32Error()); }
        try
        {
            var credential = Marshal.PtrToStructure<Credential>(pointer);
            return Marshal.PtrToStringUni(credential.CredentialBlob, checked((int)credential.CredentialBlobSize / 2));
        }
        finally { CredFree(pointer); }
    }
    public void Write(string key)
    {
        key = key.Trim();
        if (key.Length is < 10 or > 1200 || key.Any(char.IsWhiteSpace)) throw new ArgumentException("API key tidak valid.");
        nint blob = Marshal.StringToCoTaskMemUni(key);
        try
        {
            var value = new Credential { Type = 1, TargetName = Target, UserName = "LuKnight", Persist = 2,
                CredentialBlob = blob, CredentialBlobSize = (uint)Encoding.Unicode.GetByteCount(key) };
            if (!CredWrite(ref value, 0)) throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        finally { Marshal.ZeroFreeCoTaskMemUnicode(blob); }
    }
    public void Remove()
    { if (!CredDelete(Target, 1, 0) && Marshal.GetLastWin32Error() != 1168) throw new Win32Exception(Marshal.GetLastWin32Error()); }
}
