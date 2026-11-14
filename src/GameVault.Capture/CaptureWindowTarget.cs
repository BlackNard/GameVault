using System.Diagnostics;
using System.Runtime.InteropServices;
using GameVault.Core;
namespace GameVault.Capture;

internal static class CaptureWindowTarget
{
 public static nint Resolve(CaptureTarget target)
 {
  using var process=Process.GetProcessById(target.ProcessId);
  if(process.HasExited || process.StartTime.ToUniversalTime().Ticks!=target.ProcessCreatedUtcTicks)
   throw new IOException("The selected game session has ended or its process identity changed.");
  // The journal identifies the actual game PID. Never enumerate unrelated windows or guess a display.
  process.Refresh(); var window=process.MainWindowHandle;
  if(window==0 || !IsWindowVisible(window))throw new IOException("The selected game has no visible capturable window. Use windowed/borderless mode or explicitly select a monitor fallback.");
  GetWindowThreadProcessId(window,out var owner);
  if(owner!=(uint)target.ProcessId || process.HasExited)throw new IOException("The selected game window is no longer available.");
  if(IsIconic(window))throw new IOException("Restore the selected game window before capture.");
  return window;
 }
 public static void ValidateProcess(CaptureTarget target)
 {
  if(target.SessionId==Guid.Empty || target.ProcessId<=0 || target.ProcessCreatedUtcTicks<=0)throw new ArgumentException("An actual game session is required.");
  using var process=Process.GetProcessById(target.ProcessId);
  if(process.HasExited || process.StartTime.ToUniversalTime().Ticks!=target.ProcessCreatedUtcTicks)
   throw new IOException("The selected game session has ended or its process identity changed.");
 }
 [DllImport("user32.dll")]static extern bool IsWindowVisible(nint hwnd);
 [DllImport("user32.dll")]static extern bool IsIconic(nint hwnd);
 [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(nint hwnd,out uint processId);
}
