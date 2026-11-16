using System.Reflection;
namespace GameVault.App.Tests;
public sealed class BrowserDependencyTests
{
    [Fact] public void CompositionControlWindowsProjectionIsResolvableWithoutOpeningBrowser()
    {
        GameVault.App.Artwork.BrowserDependencies.Verify();
        var assembly=Assembly.Load("Microsoft.Windows.SDK.NET, Version=10.0.17763.10, Culture=neutral, PublicKeyToken=31bf3856ad364e35");
        Assert.NotNull(assembly.GetType("Windows.Graphics.Capture.GraphicsCaptureItem",throwOnError:true));
        Assert.NotNull(Assembly.Load("WinRT.Runtime"));
    }
}
