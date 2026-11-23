using System.Globalization;
using System.Text;

namespace GameVault.Infrastructure.Diagnostics;

/// <summary>What the previous run left behind. <see cref="PreviousRunEndedUnexpectedly"/> is true when its marker was never removed by a normal quit.</summary>
public sealed record CrashMarkerResult(bool PreviousRunEndedUnexpectedly, DateTimeOffset? PreviousStartedAt, string? PreviousVersion);

/// <summary>
/// A "GameVault is running" marker (Logs/running.marker). It is written at start and removed by a normal quit, so finding one at the
/// next start means the last run was killed, crashed or lost power. Never throws: a marker that cannot be written only means no notice.
/// </summary>
public static class CrashMarker
{
    public const string FileName = "running.marker";

    private static string PathFor(string root) => Path.Combine(root, "Logs", FileName);

    /// <summary>Reads the previous marker (if any) and writes a fresh one for this run. Call it only after this process owns the application folder.</summary>
    public static CrashMarkerResult Begin(string root, string version, DateTimeOffset now)
    {
        var result = new CrashMarkerResult(false, null, null);
        try
        {
            var file = PathFor(root);
            if (File.Exists(file))
            {
                DateTimeOffset? startedAt = null;
                string? previousVersion = null;
                try
                {
                    var parts = File.ReadAllText(file, Encoding.UTF8).Split('|', 2);
                    if (parts.Length >= 1 && DateTimeOffset.TryParse(parts[0], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)) startedAt = parsed;
                    if (parts.Length == 2 && parts[1].Trim().Length is > 0 and <= 40) previousVersion = parts[1].Trim();
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
                result = new CrashMarkerResult(true, startedAt, previousVersion);
            }
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, now.ToString("o", CultureInfo.InvariantCulture) + "|" + version, new UTF8Encoding(false));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        return result;
    }

    /// <summary>Removes this run's marker. Call it only from a normal quit, and only if <see cref="Begin"/> was called by this process.</summary>
    public static void EndCleanly(string root)
    {
        try { File.Delete(PathFor(root)); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }
}
