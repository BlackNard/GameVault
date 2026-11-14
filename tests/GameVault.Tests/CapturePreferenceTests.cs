using GameVault.Core;
namespace GameVault.Tests;

public sealed class CapturePreferenceTests
{
    [Fact] public void Duplicate_or_reserved_shortcuts_do_not_disturb_current_registration()
    {
        var platform=new Fake();using var keys=new CaptureHotkeySet(platform);
        Assert.True(keys.Apply(CapturePreferences.Default).Success);
        var before=platform.Calls;
        var invalid=CapturePreferences.Default with{Recording=CapturePreferences.Default.Screenshot};
        Assert.False(keys.Apply(invalid).Success);Assert.Equal(before,platform.Calls);Assert.Equal(3,keys.Registered.Count);
    }
    [Fact] public void Registration_conflict_rolls_back_previous_bindings()
    {
        var platform=new Fake();using var keys=new CaptureHotkeySet(platform);
        Assert.True(keys.Apply(CapturePreferences.Default).Success);
        platform.Refuse=0x71;
        var changed=CapturePreferences.Default with{Screenshot=new(3,0x71)};
        var result=keys.Apply(changed);
        Assert.False(result.Success);Assert.Equal(3,keys.Registered.Count);
        Assert.Equal(CapturePreferences.Default.Screenshot,keys.Registered[CaptureCommand.Screenshot]);
    }
    [Fact] public void Capture_preferences_reject_unsupported_quality_and_windows_reserved_keys()
    {
        Assert.Throws<ArgumentException>(()=>(CapturePreferences.Default with{FramesPerSecond=240}).Validate());
        Assert.Throws<ArgumentException>(()=>(CapturePreferences.Default with{Screenshot=new(8,0x73)}).Validate());
        Assert.Throws<ArgumentException>(()=>(CapturePreferences.Default with{Height=2160}).Validate());
    }
    private sealed class Fake:IHotkeyPlatform
    {
        public int Refuse,Calls;public HashSet<int> Registered=[];
        public bool Register(int id,CaptureShortcut binding){Calls++;if(binding.VirtualKey==Refuse)return false;return Registered.Add(id);}
        public void Unregister(int id){Calls++;Registered.Remove(id);}
    }
}
