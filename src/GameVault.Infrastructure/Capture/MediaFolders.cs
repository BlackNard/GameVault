using System.Globalization;
using System.Text.Json;
using GameVault.Core;
using GameVault.Infrastructure.Storage;

namespace GameVault.Infrastructure.Capture;

/// <summary>
/// The folders under a game's <c>Media</c> folder: one per play session, named after the date and time of its first capture (for example
/// <c>2026-11-18 22-45</c>, then <c>(2)</c> and so on). The session's ID is kept in a small hidden marker inside. Older folders named by the ID are still
/// recognised and are renamed once at startup.
/// </summary>
public static class MediaFolders
{
    public const string MarkerName = ".session-id";

    public static string NameFor(DateTimeOffset when) => when.ToLocalTime().ToString("yyyy-MM-dd HH-mm", CultureInfo.InvariantCulture);

    /// <summary>The session a media folder belongs to: its ID name (older layout) or its marker. Null for any other folder.</summary>
    public static Guid? Identify(string folder)
    {
        if (Guid.TryParseExact(Path.GetFileName(folder), "N", out var legacy)) return legacy == Guid.Empty ? null : legacy;
        try
        {
            var marker = new FileInfo(Path.Combine(folder, MarkerName));
            return marker.Exists && marker.Length < 256 && Guid.TryParse(File.ReadAllText(marker.FullName).Trim(), out var id) && id != Guid.Empty ? id : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>The existing folder of a session, or null.</summary>
    public static string? Find(string mediaRoot, Guid sessionId) =>
        Directory.Exists(mediaRoot) ? Directory.EnumerateDirectories(mediaRoot).Order(StringComparer.OrdinalIgnoreCase).FirstOrDefault(f => Identify(f) == sessionId) : null;

    /// <summary>A free readable folder path for a session starting at <paramref name="when"/>.</summary>
    private static string Free(string mediaRoot, DateTimeOffset when)
    {
        var name = NameFor(when);
        var candidate = Path.Combine(mediaRoot, name);
        for (var number = 2; Directory.Exists(candidate) || File.Exists(candidate); number++) candidate = Path.Combine(mediaRoot, name + " (" + number + ")");
        return candidate;
    }

    private static void WriteMarker(string folder, Guid sessionId)
    {
        var marker = Path.Combine(folder, MarkerName);
        if (File.Exists(marker)) return;
        File.WriteAllText(marker, sessionId.ToString("D"));
        File.SetAttributes(marker, FileAttributes.Hidden);
    }

    /// <summary>The session's folder, created (with its marker) when it does not exist yet.</summary>
    internal static string ForSession(ManagedPaths managed, string mediaRoot, Guid sessionId, DateTimeOffset when)
    {
        managed.EnsureDirectory(mediaRoot);
        if (Find(mediaRoot, sessionId) is { } existing) { managed.Check(existing); return existing; }
        var folder = Free(mediaRoot, when);
        managed.EnsureDirectory(folder);
        WriteMarker(folder, sessionId);
        return folder;
    }

    /// <summary>
    /// Renames ID-named session folders of every game to readable names. Runs at startup, before anything reads or records captures. Only folders are renamed;
    /// no capture file is changed, and a folder that cannot be renamed (in use, linked, or without readable capture details) is left as it is.
    /// </summary>
    public static int MigrateAll(ApplicationPaths paths)
    {
        var renamed = 0;
        var managed = new ManagedPaths(paths);
        foreach (var (_, profile) in ProfileFolders.Enumerate(paths))
        {
            try
            {
                var root = Path.Combine(profile, "Media");
                managed.Check(root);
                if (!Directory.Exists(root)) continue;
                foreach (var folder in Directory.GetDirectories(root))
                {
                    if (!Guid.TryParseExact(Path.GetFileName(folder), "N", out var sessionId) || sessionId == Guid.Empty) continue;
                    try
                    {
                        managed.Check(folder);
                        if (EarliestCapture(folder) is not { } first) continue;
                        WriteMarker(folder, sessionId);
                        Directory.Move(folder, Free(root, first));
                        renamed++;
                    }
                    catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
        return renamed;
    }

    private static DateTimeOffset? EarliestCapture(string folder)
    {
        DateTimeOffset? earliest = null;
        foreach (var file in Directory.EnumerateFiles(folder, "*.json"))
        {
            try
            {
                if (new FileInfo(file).Length > 65536) continue;
                using var document = JsonDocument.Parse(File.ReadAllBytes(file));
                if (document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty("CapturedUtc", out var value) &&
                    value.TryGetDateTimeOffset(out var when) && when != default && (earliest is null || when < earliest)) earliest = when;
            }
            catch (Exception error) when (error is JsonException or InvalidOperationException or FormatException) { }
        }
        return earliest;
    }
}
