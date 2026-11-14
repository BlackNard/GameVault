using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
namespace GameVault.App;

internal sealed class CaptureOverlay:Window
{
    private bool disposing;
    public CaptureControls Controls{get;}=new();
    public CaptureOverlay(string theme)
    {
        Title="GameVault capture";Width=310;SizeToContent=SizeToContent.Height;ResizeMode=ResizeMode.NoResize;
        WindowStyle=WindowStyle.None;ShowInTaskbar=false;ShowActivated=false;Topmost=true;
        Left=Math.Max(SystemParameters.WorkArea.Left,SystemParameters.WorkArea.Right-Width-24);Top=SystemParameters.WorkArea.Top+24;
        var border=new Border{Padding=new(16),Child=Controls,BorderThickness=new(1)};border.SetResourceReference(Border.BorderBrushProperty,"AppLine");border.SetResourceReference(Border.BackgroundProperty,"AppSurface");Content=border;
        SetResourceReference(ForegroundProperty,"AppText");ThemePalette.Apply(this,theme);
        SourceInitialized+=(_,_)=>CaptureOverlayInterop.Configure(new WindowInteropHelper(this).Handle);
        Closing+=(_,e)=>{if(!disposing){e.Cancel=true;Hide();}};
    }
    public void DisposeOverlay(){disposing=true;Close();}
}
