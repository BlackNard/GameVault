using System.Windows;
using System.Windows.Controls;
using GameVault.Core;
namespace GameVault.App;

public sealed class CaptureSettingsView:UserControl
{
    private readonly ComboBox resolution=new(){Width=130,HorizontalAlignment=HorizontalAlignment.Left};
    private readonly ComboBox rate=new(){Width=100,HorizontalAlignment=HorizontalAlignment.Left};
    private readonly CheckBox audio=new(){Content="Record game audio (Windows 11)",FontSize=14.5};
    private readonly Dictionary<CaptureCommand,(ComboBox Modifier,ComboBox Key)> shortcuts=[];
    private readonly StackPanel inputs=new();
    private readonly TextBlock notice=new(){TextWrapping=TextWrapping.Wrap,Margin=new(0,8,0,8)};
    private readonly Button apply=new(){Content="Apply capture settings",HorizontalAlignment=HorizontalAlignment.Left,Margin=new(0,8,0,8)};
    public event EventHandler<CapturePreferences>? ApplyRequested;
    public event EventHandler? InstallRuntimeRequested;
    public CaptureSettingsView()
    {
        resolution.ItemsSource=new[]{"720p","1080p"};rate.ItemsSource=new[]{"30 fps","60 fps"};
        System.Windows.Automation.AutomationProperties.SetName(resolution,"Video size");System.Windows.Automation.AutomationProperties.SetName(rate,"Frame rate");
        inputs.Children.Add(Row("Video size",resolution,"GameVault's own recorder saves MP4 (H.264) video and PNG screenshots. A game can record with NVIDIA App instead, on its Media tab."));
        inputs.Children.Add(Row("Frame rate",rate,"60 fps costs about twice the graphics power of 30. If a game stutters while recording, choose 30 fps or NVIDIA App.",true));
        audio.SetResourceReference(StyleProperty,"SettingSwitch");
        inputs.Children.Add(Framed(audio,"Only the game's own sound is recorded: never other apps, never the microphone. Off means the video is silent."));

        var keys=new WrapPanel();
        foreach(var command in Enum.GetValues<CaptureCommand>())
        {
            var label=command switch{CaptureCommand.Screenshot=>"Screenshot",CaptureCommand.Recording=>"Recording toggle",_=>"Controls toggle"};
            var modifier=new ComboBox{Width=150,Margin=new(0,0,6,0),ItemsSource=new[]{new ModChoice("Alt",1),new ModChoice("Ctrl",2),new ModChoice("Ctrl + Alt",3),new ModChoice("Ctrl + Shift",6),new ModChoice("Alt + Shift",5),new ModChoice("Ctrl + Alt + Shift",7)}};
            var key=new ComboBox{Width=86,ItemsSource=Enumerable.Range(1,12).Select(number=>"F"+number).Concat(Enumerable.Range(0x41,26).Select(value=>((char)value).ToString())).ToArray()};
            System.Windows.Automation.AutomationProperties.SetName(modifier,label+" modifiers");System.Windows.Automation.AutomationProperties.SetName(key,label+" function key");
            var pair=new StackPanel{Orientation=Orientation.Horizontal};pair.Children.Add(modifier);pair.Children.Add(key);
            var column=new StackPanel{Margin=new(0,0,18,6)};column.Children.Add(Muted(label));column.Children.Add(pair);keys.Children.Add(column);shortcuts.Add(command,(modifier,key));
        }
        var shortcutRows=new StackPanel();var heading=Title("Shortcuts",true);heading.Margin=new(0,0,0,10);shortcutRows.Children.Add(heading);shortcutRows.Children.Add(keys);
        inputs.Children.Add(Framed(shortcutRows,"They work only while a tracked game runs. Change them if an overlay, Steam or Xbox Game Bar uses the same keys. Floating controls need a windowed or borderless game."));
        inputs.Children.Add(apply);
        var root=new StackPanel();root.Children.Add(inputs);notice.SetResourceReference(TextBlock.ForegroundProperty,"AppMuted");root.Children.Add(notice);

        var runtime=new Button{Content="Install capture runtime…",HorizontalAlignment=HorizontalAlignment.Left,Margin=new(0,10,0,6)};runtime.Click+=(_,_)=>InstallRuntimeRequested?.Invoke(this,EventArgs.Empty);
        SettingHelp.SetText(runtime,"Use this if capture reports missing components. Windows N and KN also need Microsoft's Media Feature Pack.");
        var help=new StackPanel();help.Children.Add(runtime);help.Children.Add(Muted("Opens Microsoft's Visual C++ setup, which may ask for administrator approval."));
        var troubleshooting=new Expander{Header="Troubleshooting",Content=help};troubleshooting.SetResourceReference(StyleProperty,"QuietExpander");root.Children.Add(troubleshooting);
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
    private static TextBlock Muted(string text){var block=new TextBlock{Text=text,TextWrapping=TextWrapping.Wrap,Margin=new(0,0,0,4),FontSize=12.5};block.SetResourceReference(TextBlock.ForegroundProperty,"AppMuted");return block;}
    private static StackPanel Title(string text,bool mark)
    {
        var line=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};
        line.Children.Add(new TextBlock{Text=text,FontSize=14.5,TextWrapping=TextWrapping.Wrap});
        if(mark){var info=new TextBlock{Text="\uE946"};info.SetResourceReference(StyleProperty,"SettingInfo");line.Children.Add(info);}
        return line;
    }
    /// <summary>One option in the same card the other Settings pages use, with its explanation on hover.</summary>
    private static Border Framed(UIElement content,string help)
    {
        var row=new Border{Child=content};row.SetResourceReference(StyleProperty,"SettingRow");SettingHelp.SetText(row,help);return row;
    }
    private static Border Row(string title,Control control,string help,bool mark=false)
    {
        var line=new DockPanel();DockPanel.SetDock(control,Dock.Right);control.Margin=new(16,0,0,0);line.Children.Add(control);line.Children.Add(Title(title,mark));
        return Framed(line,help);
    }
    public void SetNotice(string text)=>notice.Text=text;
    public void SetEditingEnabled(bool enabled)=>inputs.IsEnabled=enabled;
    private sealed record ModChoice(string Name,uint Value){public override string ToString()=>Name;}
}
