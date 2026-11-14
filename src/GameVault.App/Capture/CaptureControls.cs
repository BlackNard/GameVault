using System.Windows;
using System.Windows.Controls;
using GameVault.Core;
using Forms=System.Windows.Forms;
namespace GameVault.App;

public sealed class CaptureControls:UserControl
{
    private readonly TextBlock target=new(){TextWrapping=TextWrapping.Wrap};
    private readonly TextBlock status=new(){TextWrapping=TextWrapping.Wrap,Margin=new(0,6,0,8)};
    private readonly Button screenshot=new(){Content="Screenshot",Margin=new(0,0,6,6)};
    private readonly Button recording=new(){Content="Record video",Margin=new(0,0,6,6)};
    private readonly Button overlay=new(){Content="Show controls",Margin=new(0,0,0,6)};
    private readonly CheckBox fallback=new(){Content="Capture an entire monitor instead",Margin=new(0,8,0,6)};
    private readonly ComboBox monitors=new(){Visibility=Visibility.Collapsed,Margin=new(0,0,0,6)};
    private readonly TextBlock privacy=new(){Text="Monitor capture includes other apps on that display. Choose a monitor explicitly.",TextWrapping=TextWrapping.Wrap,Visibility=Visibility.Collapsed,Margin=new(0,0,0,8)};
    private readonly StackPanel source=new();
    public event EventHandler<CaptureCommand>? Command;
    public CaptureControls()
    {
        var root=new StackPanel();var heading=new TextBlock{Text="Gameplay capture",FontWeight=FontWeights.SemiBold};
        root.Children.Add(heading);root.Children.Add(target);root.Children.Add(status);
        var buttons=new WrapPanel();buttons.Children.Add(screenshot);buttons.Children.Add(recording);buttons.Children.Add(overlay);root.Children.Add(buttons);
        source.Children.Add(fallback);source.Children.Add(monitors);source.Children.Add(privacy);root.Children.Add(source);
        foreach(var text in new[]{target,status,privacy})text.SetResourceReference(TextBlock.ForegroundProperty,"AppMuted");
        screenshot.Click+=(_,_)=>Command?.Invoke(this,CaptureCommand.Screenshot);
        recording.Click+=(_,_)=>Command?.Invoke(this,CaptureCommand.Recording);
        overlay.Click+=(_,_)=>Command?.Invoke(this,CaptureCommand.Overlay);
        fallback.Checked+=(_,_)=>
        {
            monitors.ItemsSource=Forms.Screen.AllScreens.Select(screen=>new MonitorChoice(screen.DeviceName,$"{screen.DeviceName} · {screen.Bounds.Width}×{screen.Bounds.Height}")).ToArray();
            monitors.SelectedIndex=-1;monitors.Visibility=privacy.Visibility=Visibility.Visible;
        };
        fallback.Unchecked+=(_,_)=>{monitors.SelectedIndex=-1;monitors.Visibility=privacy.Visibility=Visibility.Collapsed;};
        System.Windows.Automation.AutomationProperties.SetName(screenshot,"Take game screenshot");
        System.Windows.Automation.AutomationProperties.SetName(recording,"Start or stop game recording");
        System.Windows.Automation.AutomationProperties.SetName(monitors,"Explicit monitor capture target");
        Content=root;
    }
    public string? ExplicitMonitor
    {
        get
        {
            if(fallback.IsChecked!=true)return null;
            return (monitors.SelectedItem as MonitorChoice)?.Device??throw new InvalidOperationException("Choose a monitor, or turn off monitor capture to capture only the game window.");
        }
    }
    public void Update(string game,string message,bool running,bool active,bool busy,bool compact=false)
    {
        target.Text=game;status.Text=message;recording.Content=active?"Stop recording":"Record video";
        screenshot.IsEnabled=running&&!busy;recording.IsEnabled=(running||active)&&!busy;overlay.IsEnabled=!busy;
        source.Visibility=compact?Visibility.Collapsed:Visibility.Visible;source.IsEnabled=!active&&!busy;
        overlay.Content=compact?"Hide controls":"Show controls";
        foreach(var button in new[]{screenshot,recording,overlay})button.Focusable=!compact;
    }
    private sealed record MonitorChoice(string Device,string Description){public override string ToString()=>Description;}
}
