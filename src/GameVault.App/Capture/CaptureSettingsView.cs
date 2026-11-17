using System.Windows;
using System.Windows.Controls;
using GameVault.Core;
namespace GameVault.App;

public sealed class CaptureSettingsView:UserControl
{
    private readonly ComboBox resolution=new(){Width=130,HorizontalAlignment=HorizontalAlignment.Left};
    private readonly ComboBox rate=new(){Width=100,HorizontalAlignment=HorizontalAlignment.Left};
    private readonly CheckBox audio=new(){Content="Record only this game's audio (Windows 11)",Margin=new(0,8,0,8)};
    private readonly Dictionary<CaptureCommand,(ComboBox Modifier,ComboBox Key)> shortcuts=[];
    private readonly StackPanel inputs=new();
    private readonly TextBlock notice=new(){TextWrapping=TextWrapping.Wrap,Margin=new(0,8,0,8)};
    private readonly Button apply=new(){Content="Apply capture settings",HorizontalAlignment=HorizontalAlignment.Left,Margin=new(0,8,0,8)};
    public event EventHandler<CapturePreferences>? ApplyRequested;
    public event EventHandler? InstallRuntimeRequested;
    public CaptureSettingsView()
    {
        inputs.Children.Add(new TextBlock{Text="Capture and shortcuts",FontSize=22,FontWeight=FontWeights.SemiBold,Margin=new(0,0,0,12)});
        inputs.Children.Add(new TextBlock{Text="PNG screenshots · MP4 H.264 video. Microphone capture is never enabled.",TextWrapping=TextWrapping.Wrap,Margin=new(0,0,0,12)});
        inputs.Children.Add(new TextBlock{Text="These settings are for GameVault's own recorder. On a PC with NVIDIA App, a game can record with NVIDIA instead (choose it on the game's Media tab); GameVault then copies the clips into that game's profile.",TextWrapping=TextWrapping.Wrap,Margin=new(0,0,0,12)});
        inputs.Children.Add(new TextBlock{Text="Shortcuts only work while a tracked game is running. If your GPU overlay (NVIDIA, AMD), Steam or Xbox Game Bar uses the same keys, choose different ones here.",TextWrapping=TextWrapping.Wrap,Margin=new(0,0,0,12)});
        resolution.ItemsSource=new[]{"720p","1080p"};rate.ItemsSource=new[]{"30 fps","60 fps"};
        inputs.Children.Add(new Label{Content="Video size"});inputs.Children.Add(resolution);inputs.Children.Add(new Label{Content="Frame rate"});inputs.Children.Add(rate);inputs.Children.Add(audio);
        foreach(var command in Enum.GetValues<CaptureCommand>())
        {
            var label=command switch{CaptureCommand.Screenshot=>"Screenshot",CaptureCommand.Recording=>"Recording toggle",_=>"Controls toggle"};
            inputs.Children.Add(new TextBlock{Text=label,Margin=new(0,8,0,4)});
            var modifier=new ComboBox{Width=180,Margin=new(0,0,8,0),ItemsSource=new[]{new ModChoice("Alt",1),new ModChoice("Ctrl",2),new ModChoice("Ctrl + Alt",3),new ModChoice("Ctrl + Shift",6),new ModChoice("Alt + Shift",5),new ModChoice("Ctrl + Alt + Shift",7)}};
            var key=new ComboBox{Width=90,ItemsSource=Enumerable.Range(1,12).Select(number=>"F"+number).Concat(Enumerable.Range(0x41,26).Select(value=>((char)value).ToString())).ToArray()};
            System.Windows.Automation.AutomationProperties.SetName(modifier,label+" modifiers");System.Windows.Automation.AutomationProperties.SetName(key,label+" function key");
            var row=new StackPanel{Orientation=Orientation.Horizontal};row.Children.Add(modifier);row.Children.Add(key);inputs.Children.Add(row);shortcuts.Add(command,(modifier,key));
        }
        inputs.Children.Add(apply);var root=new StackPanel();root.Children.Add(inputs);notice.SetResourceReference(TextBlock.ForegroundProperty,"AppMuted");root.Children.Add(notice);
        root.Children.Add(new TextBlock{Text="Floating controls work with windowed and borderless games. For unsupported exclusive-fullscreen/elevated games, use launcher controls or shortcuts. Monitor capture is a separate explicit choice.",TextWrapping=TextWrapping.Wrap,Margin=new(0,0,0,12)});
        var runtime=new Button{Content="Install capture runtime…",HorizontalAlignment=HorizontalAlignment.Left};runtime.Click+=(_,_)=>InstallRuntimeRequested?.Invoke(this,EventArgs.Empty);root.Children.Add(runtime);
        root.Children.Add(new TextBlock{Text="If capture reports missing components, this opens the bundled Microsoft Visual C++ x64 setup and may ask for administrator approval. Windows N/KN also needs Microsoft's Media Feature Pack.",TextWrapping=TextWrapping.Wrap,Margin=new(0,8,0,24)});
        apply.Click+=(_,_)=>
        {
            try
            {
                CaptureShortcut Get(CaptureCommand command){var row=shortcuts[command];return new((row.Modifier.SelectedItem as ModChoice)?.Value??0,row.Key.SelectedIndex<12?0x70+row.Key.SelectedIndex:0x41+row.Key.SelectedIndex-12);}
                var value=new CapturePreferences(1,resolution.SelectedIndex==0?720:1080,rate.SelectedIndex==1?60:30,audio.IsChecked==true,Get(CaptureCommand.Screenshot),Get(CaptureCommand.Recording),Get(CaptureCommand.Overlay));
                value.Validate();ApplyRequested?.Invoke(this,value);
            }catch(ArgumentException error){notice.Text=error.Message;}
        };
        Content=root;SetPreferences(CapturePreferences.Default);
    }
    public void SetPreferences(CapturePreferences value)
    {
        resolution.SelectedIndex=value.Height==720?0:1;rate.SelectedIndex=value.FramesPerSecond==60?1:0;audio.IsChecked=value.GameAudio;
        foreach(var (command,binding) in new[]{(CaptureCommand.Screenshot,value.Screenshot),(CaptureCommand.Recording,value.Recording),(CaptureCommand.Overlay,value.Overlay)})
        {var row=shortcuts[command];row.Modifier.SelectedItem=row.Modifier.Items.Cast<ModChoice>().FirstOrDefault(item=>item.Value==binding.Modifiers);row.Key.SelectedIndex=binding.VirtualKey>=0x70?binding.VirtualKey-0x70:12+binding.VirtualKey-0x41;}
    }
    public void SetNotice(string text)=>notice.Text=text;
    public void SetEditingEnabled(bool enabled)=>inputs.IsEnabled=enabled;
    private sealed record ModChoice(string Name,uint Value){public override string ToString()=>Name;}
}
