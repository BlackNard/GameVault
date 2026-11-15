using System.Reflection;
namespace GameVault.App.Artwork;
internal static class BrowserDependencies
{
    public static void Verify()
    {
        var projection=Assembly.Load("Microsoft.Windows.SDK.NET, Version=10.0.17763.10, Culture=neutral, PublicKeyToken=31bf3856ad364e35");
        _=projection.GetType("Windows.Graphics.Capture.GraphicsCaptureItem",throwOnError:true);
        _=Assembly.Load("WinRT.Runtime");
    }
}
