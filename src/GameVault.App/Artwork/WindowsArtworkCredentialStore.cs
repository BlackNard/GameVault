using System.Runtime.InteropServices;
using System.Text;
using GameVault.Core;
namespace GameVault.App.Artwork;

/// <summary>Per-Windows-user generic credential; never stored alongside portable app data.</summary>
internal sealed class WindowsArtworkCredentialStore : IArtworkCredentialStore
{
    private const string Target = "GameVault/SteamGridDB/v2";
    public string? Read()
    {
        if(!CredRead(Target,1,0,out var pointer))
        {
            if(Marshal.GetLastWin32Error()==1168)return null;
            throw new InvalidOperationException("Windows could not read the artwork credential.");
        }
        try { var credential=Marshal.PtrToStructure<Credential>(pointer);if(credential.Size>1024)throw new InvalidOperationException("Invalid artwork credential.");var bytes=new byte[credential.Size];Marshal.Copy(credential.Blob,bytes,0,bytes.Length);try{return Encoding.UTF8.GetString(bytes);}finally{Array.Clear(bytes);} }
        finally{CredFree(pointer);}
    }
    public void Write(string key)
    {
        key=key.Trim();if(key.Length is < 16 or > 256 ||key.Any(c=>!char.IsAsciiLetterOrDigit(c)))throw new ArgumentException("Enter a valid personal SteamGridDB API key.");
        var bytes=Encoding.UTF8.GetBytes(key);var pointer=Marshal.AllocHGlobal(bytes.Length);
        try {Marshal.Copy(bytes,0,pointer,bytes.Length);var credential=new Credential{Type=1,Target=Target,Size=(uint)bytes.Length,Blob=pointer,Persist=2,UserName="SteamGridDB"};if(!CredWrite(ref credential,0))throw new InvalidOperationException("Windows could not save the artwork credential.");}
        finally{for(var i=0;i<bytes.Length;i++)Marshal.WriteByte(pointer,i,0);Marshal.FreeHGlobal(pointer);Array.Clear(bytes);}
    }
    public void Delete(){if(!CredDelete(Target,1,0)&&Marshal.GetLastWin32Error()!=1168)throw new InvalidOperationException("Windows could not remove the artwork credential.");}
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] private struct Credential
    {public uint Flags,Type;public string Target;public string? Comment;public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;public uint Size;public IntPtr Blob;public uint Persist,AttributeCount;public IntPtr Attributes;public string? Alias,UserName;}
    [DllImport("advapi32",EntryPoint="CredReadW",CharSet=CharSet.Unicode,SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)] private static extern bool CredRead(string target,uint type,uint flags,out IntPtr credential);
    [DllImport("advapi32",EntryPoint="CredWriteW",CharSet=CharSet.Unicode,SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)] private static extern bool CredWrite(ref Credential credential,uint flags);
    [DllImport("advapi32",EntryPoint="CredDeleteW",CharSet=CharSet.Unicode,SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)] private static extern bool CredDelete(string target,uint type,uint flags);
    [DllImport("advapi32")] private static extern void CredFree(IntPtr buffer);
}
