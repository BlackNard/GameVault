namespace GameVault.Core;

public enum CaptureCommand { Screenshot=1, Recording=2, Overlay=3 }
public sealed record CaptureShortcut(uint Modifiers,int VirtualKey)
{
    public void Validate()
    {
        if((Modifiers&~7u)!=0 || (Modifiers&3u)==0 || !(VirtualKey is >=0x70 and <=0x7B or >=0x41 and <=0x5A) || (VirtualKey==0x73 && (Modifiers&1)!=0 && (Modifiers&2)==0))
            throw new ArgumentException("Choose Ctrl or Alt with an F1–F12 key; Windows-reserved modifiers are not supported.");
    }
    public override string ToString()=>((Modifiers&2)!=0?"Ctrl+":"")+((Modifiers&1)!=0?"Alt+":"")+((Modifiers&4)!=0?"Shift+":"")+(VirtualKey>=0x70?"F"+(VirtualKey-0x70+1):((char)VirtualKey).ToString());
}
public sealed record CapturePreferences(int SchemaVersion,int Height,int FramesPerSecond,bool GameAudio,
    CaptureShortcut Screenshot,CaptureShortcut Recording,CaptureShortcut Overlay)
{
    // Ctrl+Shift+F9/F10/F11: avoids the NVIDIA overlay (Alt+Z, Alt+F-keys), Steam (F12, Shift+Tab) and Xbox Game Bar (Win+Alt+...) defaults.
    public static CapturePreferences Default=>new(1,1080,30,false,new(6,0x78),new(6,0x79),new(6,0x7A));
    // Earlier defaults (Ctrl+Alt+F9/F10 or Alt+F9/F10, and Alt+Z) overlap NVIDIA's overlay controls.
    private static readonly CaptureShortcut[] LegacyScreenshot=[new(3,0x78),new(1,0x78)];
    private static readonly CaptureShortcut[] LegacyRecording=[new(3,0x79),new(1,0x79)];
    private static readonly CaptureShortcut LegacyOverlay=new(1,0x5A);
    /// <summary>Moves bindings that are still the old overlapping defaults to the current defaults; any customised binding set is returned unchanged.</summary>
    public CapturePreferences WithLegacyDefaultsUpgraded()
    {
        if(!LegacyScreenshot.Contains(Screenshot) || !LegacyRecording.Contains(Recording) || Overlay!=LegacyOverlay)return this;
        var current=Default;
        return this with{Screenshot=current.Screenshot,Recording=current.Recording,Overlay=current.Overlay};
    }
    public void Validate()
    {
        if(SchemaVersion!=1 || Height is not(720 or 1080) || FramesPerSecond is not(30 or 60) || Screenshot is null || Recording is null || Overlay is null)
            throw new ArgumentException("Choose 720p or 1080p and 30 or 60 fps.");
        Screenshot.Validate();Recording.Validate();Overlay.Validate();
        if(new[]{Screenshot,Recording,Overlay}.Distinct().Count()!=3)throw new ArgumentException("Each capture action needs a different shortcut.");
    }
}
public interface IHotkeyPlatform
{
    bool Register(int id,CaptureShortcut binding);
    void Unregister(int id);
}
public sealed record HotkeyApplyResult(bool Success,string Message);
public sealed class CaptureHotkeySet(IHotkeyPlatform platform):IDisposable
{
    private readonly object gate=new();
    private readonly Dictionary<CaptureCommand,CaptureShortcut> registered=[];
    public IReadOnlyDictionary<CaptureCommand,CaptureShortcut> Registered{get{lock(gate)return new Dictionary<CaptureCommand,CaptureShortcut>(registered);}}
    public const int IdBase=0x4750;
    public HotkeyApplyResult Apply(CapturePreferences preferences,bool sessionActive=true)
    {
        try{preferences.Validate();}catch(ArgumentException error){return new(false,error.Message);}
        lock(gate)
        {
            if(!sessionActive){UnregisterAll();return new(true,"Capture shortcuts become active when a tracked game is running.");}
            var old=registered.ToArray();UnregisterAll();
            var requested=new[]{(CaptureCommand.Screenshot,preferences.Screenshot),(CaptureCommand.Recording,preferences.Recording),(CaptureCommand.Overlay,preferences.Overlay)};
            foreach(var (command,binding) in requested)
            {
                if(platform.Register(IdBase+(int)command,binding)){registered.Add(command,binding);continue;}
                UnregisterAll();
                foreach(var previous in old)if(platform.Register(IdBase+(int)previous.Key,previous.Value))registered.Add(previous.Key,previous.Value);
                return new(false,$"{command} shortcut {binding} is unavailable. Choose another combination. {registered.Count} previous shortcut(s) remain active; launcher and overlay buttons are available.");
            }
            return new(true,"Capture shortcuts applied.");
        }
    }
    private void UnregisterAll(){foreach(var command in registered.Keys)platform.Unregister(IdBase+(int)command);registered.Clear();}
    public bool IsRegistered(CaptureCommand command){lock(gate)return registered.ContainsKey(command);}
    public void Dispose(){lock(gate)UnregisterAll();}
}
