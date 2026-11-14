namespace GameVault.Core;

public enum CaptureAudioMode { None, ProcessOnly }
public enum CaptureState { Idle, Starting, Recording, Stopping, Failed }
public enum CaptureMediaStatus { Incomplete, Complete }
public enum CaptureMediaKind { Screenshot, Video }
// MonitorDeviceName is an explicitly selected fallback, never inferred by the backend.
public sealed record CaptureTarget(Guid SessionId, int ProcessId, long ProcessCreatedUtcTicks, string? MonitorDeviceName = null);
public sealed record CaptureOptions(CaptureAudioMode Audio = CaptureAudioMode.None, bool HardwareEncoding = true,
 int Width = 1920, int Height = 1080, int FramesPerSecond = 30, int Bitrate = 8_000_000)
{
 public void Validate()
 {
  if(!Enum.IsDefined(Audio) || Width<2 || Width>1920 || Height<2 || Height>1080 || Width%2!=0 || Height%2!=0 ||
   FramesPerSecond is not (30 or 60) || Bitrate<1_000_000 || Bitrate>20_000_000)
   throw new ArgumentException("Choose an even capture size up to 1920×1080, 30/60 fps and 1–20 Mbps.");
 }
}
public sealed record CaptureMedia(Guid Id, Guid ProfileId, Guid SessionId, DateTimeOffset CapturedUtc,
 CaptureMediaKind Kind, CaptureMediaStatus Status, string FilePath, string? Error = null);
