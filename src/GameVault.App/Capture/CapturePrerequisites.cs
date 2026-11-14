using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
namespace GameVault.App;

internal static class CapturePrerequisites
{
    public static void OpenRuntimeInstaller(string root)
    {
        using var metadata=typeof(CapturePrerequisites).Assembly.GetManifestResourceStream("GameVault.CaptureRuntime.json")
            ??throw new IOException("Capture runtime information is unavailable. Reinstall GameVault.");
        using var document=JsonDocument.Parse(metadata);
        var name=document.RootElement.GetProperty("fileName").GetString()!;
        if(name!="VC_redist.x64.exe")throw new IOException("Unexpected capture runtime name.");
        var path=Path.Combine(root,"Prerequisites",name);
        using var lease=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);
        if(Convert.ToHexString(SHA256.HashData(lease)).ToLowerInvariant()!=document.RootElement.GetProperty("sha256").GetString())
            throw new IOException("The bundled Microsoft runtime did not pass its integrity check. Reinstall GameVault.");
        Process.Start(new ProcessStartInfo(path){UseShellExecute=true,Verb="runas",Arguments="/install /norestart"});
    }
}
