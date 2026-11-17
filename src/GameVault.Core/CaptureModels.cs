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
// SourceFingerprint is set only for clips copied in from another recorder (NVIDIA App); it identifies the original file so it is never imported twice.
public sealed record CaptureMedia(Guid Id, Guid ProfileId, Guid SessionId, DateTimeOffset CapturedUtc,
 CaptureMediaKind Kind, CaptureMediaStatus Status, string FilePath, string? Error = null, string? SourceFingerprint = null, string? Warning = null);

/// <summary>Which recorder a game uses. Auto means NVIDIA App when it is installed, otherwise GameVault's own recorder.</summary>
public enum CaptureSource { Auto, GameVault, NvidiaApp }
public sealed record CaptureSourceSettings(int SchemaVersion, Guid GameId, CaptureSource Source);
public sealed record NvidiaAppInfo(bool Installed, bool Running, string? CaptureFolder)
{
 public static readonly NvidiaAppInfo NotFound = new(false, false, null);
 public string Describe() => !Installed ? "NVIDIA App was not found." : Running ? "NVIDIA App detected and running." : "NVIDIA App detected (not running).";
}
/// <summary>Outcome of copying NVIDIA App captures into a game's profile.</summary>
public sealed record NvidiaImportResult(int Imported, int AlreadyImported, int Waiting, int Unsupported, int Failed, string? FirstError = null)
{
 public static readonly NvidiaImportResult None = new(0, 0, 0, 0, 0);
 public string Describe(string folder)
 {
  var parts = new List<string>();
  if (Imported > 0) parts.Add($"{Imported} new capture(s) copied");
  if (AlreadyImported > 0) parts.Add($"{AlreadyImported} already in the profile");
  if (Waiting > 0) parts.Add($"{Waiting} still being written (try again shortly)");
  if (Unsupported > 0) parts.Add($"{Unsupported} in an unsupported format were skipped");
  if (Failed > 0) parts.Add($"{Failed} could not be copied" + (FirstError is null ? "" : ": " + FirstError));
  return parts.Count == 0 ? "No NVIDIA captures from your play sessions were found in " + folder + "." : string.Join(" · ", parts) + ".";
 }
}
