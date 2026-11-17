using GameVault.Core;
namespace GameVault.Tests;
public sealed class SessionCaptureShortcutTests
{
 [Fact] public void Alt_z_is_still_a_valid_custom_binding_and_describes_itself_clearly(){var key=new CaptureShortcut(1,0x5A);key.Validate();Assert.Equal("Alt+Z",key.ToString());}
 [Fact] public void Defaults_avoid_nvidia_steam_and_game_bar_overlay_keys()
 {
  var d=CapturePreferences.Default;d.Validate();
  Assert.Equal(["Ctrl+Shift+F9","Ctrl+Shift+F10","Ctrl+Shift+F11"],new[]{d.Screenshot,d.Recording,d.Overlay}.Select(s=>s.ToString()));
  foreach(var s in new[]{d.Screenshot,d.Recording,d.Overlay})Assert.Equal(6u,s.Modifiers); // never a bare Alt/Ctrl+Alt combination, Alt+Z, F12 or Shift+Tab
 }
 [Theory]
 [InlineData(3u,0x78,3u,0x79)] // Ctrl+Alt+F9 / F10
 [InlineData(1u,0x78,1u,0x79)] // Alt+F9 / F10 (the owner's saved overlapping set)
 [InlineData(3u,0x78,1u,0x79)]
 public void Old_overlapping_defaults_are_upgraded_and_other_settings_kept(uint shotMod,int shotKey,uint recMod,int recKey)
 {
  var old=new CapturePreferences(1,720,60,true,new(shotMod,shotKey),new(recMod,recKey),new(1,0x5A));
  var upgraded=old.WithLegacyDefaultsUpgraded();
  upgraded.Validate();
  Assert.Equal(CapturePreferences.Default.Screenshot,upgraded.Screenshot);Assert.Equal(CapturePreferences.Default.Recording,upgraded.Recording);Assert.Equal(CapturePreferences.Default.Overlay,upgraded.Overlay);
  Assert.Equal((720,60,true),(upgraded.Height,upgraded.FramesPerSecond,upgraded.GameAudio));
 }
 [Fact] public void Customised_bindings_are_never_changed()
 {
  var custom=new CapturePreferences(1,1080,30,false,new(3,0x78),new(2,0x75),new(1,0x5A));
  Assert.Same(custom,custom.WithLegacyDefaultsUpgraded());
  var current=CapturePreferences.Default;
  Assert.Same(current,current.WithLegacyDefaultsUpgraded());
 }
 [Fact] public void Inactive_session_releases_every_registered_shortcut(){var p=new Fake();using var keys=new CaptureHotkeySet(p);Assert.True(keys.Apply(CapturePreferences.Default).Success);Assert.Equal(3,p.Ids.Count);Assert.True(keys.Apply(CapturePreferences.Default,false).Success);Assert.Empty(p.Ids);Assert.Empty(keys.Registered);}
 [Fact] public void Windows_close_binding_is_rejected(){Assert.Throws<ArgumentException>(()=>new CaptureShortcut(1,0x73).Validate());}
 private sealed class Fake:IHotkeyPlatform{public HashSet<int> Ids=[];public bool Register(int id,CaptureShortcut binding)=>Ids.Add(id);public void Unregister(int id)=>Ids.Remove(id);}
}
