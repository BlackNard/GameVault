using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using GameVault.Core;
namespace GameVault.App;

internal sealed class CaptureHotkeyPlatform(nint window):IHotkeyPlatform
{
    public bool Register(int id,CaptureShortcut binding)=>RegisterHotKey(window,id,binding.Modifiers|0x4000u,(uint)binding.VirtualKey);
    public void Unregister(int id)=>UnregisterHotKey(window,id);
    [DllImport("user32.dll",SetLastError=true)]private static extern bool RegisterHotKey(nint hwnd,int id,uint modifiers,uint key);
    [DllImport("user32.dll",SetLastError=true)]private static extern bool UnregisterHotKey(nint hwnd,int id);
}
internal static class CaptureOverlayInterop
{
    public static void Configure(nint window)
    {
        var style=GetWindowLongPtr(window,-20).ToInt64();
        SetWindowLongPtr(window,-20,(nint)(style|0x08000000|0x00000080)); // no activation, tool window
        _=SetWindowDisplayAffinity(window,0x11); // exclude our controls where Windows supports it
    }
    [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")]private static extern nint GetWindowLongPtr(nint hwnd,int index);
    [DllImport("user32.dll",EntryPoint="SetWindowLongPtrW")]private static extern nint SetWindowLongPtr(nint hwnd,int index,nint value);
    [DllImport("user32.dll")]private static extern bool SetWindowDisplayAffinity(nint hwnd,uint affinity);
}
