using GameVault.Core;
namespace GameVault.Tests;
public sealed class SessionCaptureShortcutTests
{
 [Fact] public void Overlay_supports_alt_z_and_describes_it_clearly(){var key=new CaptureShortcut(1,0x5A);key.Validate();Assert.Equal("Alt+Z",key.ToString());Assert.Equal(key,CapturePreferences.Default.Overlay);}
 [Fact] public void Inactive_session_releases_every_registered_shortcut(){var p=new Fake();using var keys=new CaptureHotkeySet(p);Assert.True(keys.Apply(CapturePreferences.Default).Success);Assert.Equal(3,p.Ids.Count);Assert.True(keys.Apply(CapturePreferences.Default,false).Success);Assert.Empty(p.Ids);Assert.Empty(keys.Registered);}
 [Fact] public void Windows_close_binding_is_rejected(){Assert.Throws<ArgumentException>(()=>new CaptureShortcut(1,0x73).Validate());}
 private sealed class Fake:IHotkeyPlatform{public HashSet<int> Ids=[];public bool Register(int id,CaptureShortcut binding)=>Ids.Add(id);public void Unregister(int id)=>Ids.Remove(id);}
}
