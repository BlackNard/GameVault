using GameVault.Core;
namespace GameVault.App;

/// <summary>Wording of the capture status line, kept separate from the window so it can be tested.</summary>
public static class CaptureStatusText
{
    public static string Audio(bool gameAudio) => gameAudio ? "game audio on" : "no audio";

    public static string Recording(TimeSpan elapsed, VideoHealth health, bool gameAudio)
    {
        var time = $"{Math.Max(0, (long)elapsed.TotalHours):00}:{elapsed.Minutes:00}:{elapsed.Seconds:00}";
        var stall = health.Stalled
            ? $" · video stalled {Math.Max(1, (int)health.CurrentStall.TotalSeconds)} s" + (gameAudio ? " (audio continues)" : "")
            : health.Episodes > 0 ? $" · video paused {health.Episodes}×" : "";
        return $"Recording · {time}{stall} · {Audio(gameAudio)}" + (gameAudio ? "" : " (turn on game audio in Settings)");
    }

    public static string Idle(bool gameAudio) => "PNG screenshots · MP4 recordings · " + Audio(gameAudio);
}

