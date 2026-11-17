namespace GameVault.Core;

/// <summary>What GameVault knows about the video half of a running recording.</summary>
public sealed record VideoHealth(bool Monitoring, bool Stalled, TimeSpan CurrentStall, int Episodes, TimeSpan TotalStalled)
{
 /// <summary>Nothing known (the recorder cannot report frames, or no frame has arrived yet): never raises an alarm.</summary>
 public static readonly VideoHealth Unknown = new(false, false, TimeSpan.Zero, 0, TimeSpan.Zero);
 public string? Warning => Episodes == 0 ? null :
  $"Video stopped {Episodes} time(s) for about {Math.Max(1, (int)Math.Round(TotalStalled.TotalSeconds))} s in total while audio kept recording, so this clip has frozen picture. Game window changes (fullscreen, loading, alt-tab) or an overloaded PC can cause this.";
}

/// <summary>
/// Notices when a recording stops receiving video frames (the picture freezes while audio continues, as in the TEKKEN 8 report).
/// It only starts watching after the first frame has been seen, so a recorder that cannot count frames never causes a false alarm.
/// </summary>
public sealed class RecordingWatchdog(TimeSpan? stallAfter = null)
{
 readonly TimeSpan threshold = stallAfter ?? TimeSpan.FromSeconds(4);
 bool armed, stalled; long lastFrames; DateTimeOffset lastProgress, stallStart; int episodes; TimeSpan finished;
 public VideoHealth Observe(long frames, DateTimeOffset now)
 {
  if (frames < 0) return VideoHealth.Unknown;
  if (!armed)
  {
   if (frames > 0) { armed = true; lastFrames = frames; lastProgress = now; }
   return Snapshot(now);
  }
  if (frames > lastFrames)
  {
   if (stalled) { finished += now - stallStart; stalled = false; }
   lastFrames = frames; lastProgress = now;
  }
  else if (!stalled && now - lastProgress >= threshold) { stalled = true; stallStart = lastProgress; episodes++; }
  return Snapshot(now);
 }
 VideoHealth Snapshot(DateTimeOffset now)
 {
  var current = stalled ? now - stallStart : TimeSpan.Zero;
  return new(armed, stalled, current, episodes, finished + current);
 }
}
