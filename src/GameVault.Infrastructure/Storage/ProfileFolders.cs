using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using GameVault.Core;

namespace GameVault.Infrastructure.Storage;

/// <summary>
/// Maps a game's stable ID to its profile folder. New and migrated folders are named after the game
/// (<c>Profiles/TEKKEN 8</c>); older folders named by the GUID (<c>Profiles/4b8547bf...</c>) are still
/// recognised and are renamed once. The stable ID always lives in <c>profile.json</c>, never in the folder name.
/// </summary>
public static class ProfileFolders
{
    private static readonly ConcurrentDictionary<string, ConcurrentDictionary<Guid, string>> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };
    private const int MaxNameLength = 80;
    private const long MaxProfileJsonBytes = 1024 * 1024;

    /// <summary>The pre-naming layout: a folder named by the GUID. Used when no folder for the game exists yet.</summary>
    public static string Legacy(ApplicationPaths paths, Guid id) => Path.Combine(paths.Profiles, id.ToString("N"));

    /// <summary>The existing folder of this game, or the legacy GUID path when none exists. Never creates anything.</summary>
    public static string Resolve(ApplicationPaths paths, Guid id)
    {
        var map = Map(paths);
        if (map.TryGetValue(id, out var cached) && Directory.Exists(cached)) return cached;
        map.TryRemove(id, out _);
        foreach (var entry in Enumerate(paths)) if (entry.Id == id) return entry.Folder;
        return Legacy(paths, id);
    }

    /// <summary>Every profile folder with the game ID it belongs to. Id is null when the folder cannot be identified or repeats another folder's ID.</summary>
    public static (Guid? Id, string Folder)[] Enumerate(ApplicationPaths paths)
    {
        if (!Directory.Exists(paths.Profiles)) return [];
        var fresh = new ConcurrentDictionary<Guid, string>();
        var result = new List<(Guid? Id, string Folder)>();
        foreach (var folder in Directory.GetDirectories(paths.Profiles).Order(StringComparer.OrdinalIgnoreCase))
        {
            var id = IdOf(folder);
            if (id is Guid value && fresh.TryAdd(value, folder)) result.Add((value, folder));
            else result.Add((null, folder));
        }
        Cache[paths.Root] = fresh;
        return result.ToArray();
    }

    /// <summary>A free folder path for a game, named after it. Duplicate names get " (2)", " (3)"...</summary>
    public static string ForNew(ApplicationPaths paths, string name)
    {
        var baseName = SafeName(name);
        var candidate = Path.Combine(paths.Profiles, baseName);
        for (var number = 2; Directory.Exists(candidate) || File.Exists(candidate); number++)
            candidate = Path.Combine(paths.Profiles, baseName + " (" + number + ")");
        return candidate;
    }

    /// <summary>Remember a folder that was just created for a game.</summary>
    public static void Register(ApplicationPaths paths, Guid id, string folder) => Map(paths)[id] = folder;

    /// <summary>
    /// Records the stable ID inside the folder so a name-based folder is still identified if profile.json is damaged
    /// or from a newer version. Never replaces an existing marker.
    /// </summary>
    public static void EnsureMarker(string folder, Guid id)
    {
        try
        {
            var marker = Path.Combine(folder, MarkerFile);
            if (!File.Exists(marker)) File.WriteAllText(marker, id.ToString("D") + Environment.NewLine);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }

    private const string MarkerFile = ".gamevault-id";

    /// <summary>
    /// Makes the folder name match the game's name, renaming it if needed. A locked or otherwise
    /// unavailable folder is simply left as it is (the ID lookup keeps working) and is retried next time.
    /// </summary>
    public static string Normalize(ApplicationPaths paths, Guid id, string name)
    {
        var current = Resolve(paths, id);
        if (!Directory.Exists(current)) return current;
        EnsureMarker(current, id);
        if (Matches(Path.GetFileName(current), SafeName(name))) return current;
        try
        {
            var target = ForNew(paths, name);
            Directory.Move(current, target);
            Register(paths, id, target);
            return target;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return current;
        }
    }

    /// <summary>True when a folder name is the game's name, optionally followed by a duplicate counter.</summary>
    public static bool Matches(string folderName, string safeName) =>
        string.Equals(folderName, safeName, StringComparison.OrdinalIgnoreCase) ||
        Regex.IsMatch(folderName, "^" + Regex.Escape(safeName) + @" \(\d+\)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>A Windows-safe folder name for a game: no invalid or control characters, no reserved device names, no leading/trailing dots or spaces.</summary>
    public static string SafeName(string? name)
    {
        var text = new StringBuilder();
        foreach (var c in name ?? "")
            text.Append(c < ' ' || "<>:\"/\\|?*".Contains(c) ? ' ' : c);
        var value = Regex.Replace(text.ToString(), @"\s+", " ").Trim(' ', '.');
        if (value.Length > MaxNameLength) value = value[..MaxNameLength].Trim(' ', '.');
        if (value.Length == 0) value = "Game";
        var stem = value.Split('.')[0].TrimEnd(' ');
        if (Reserved.Contains(stem) || Guid.TryParseExact(value, "N", out _)) value = "Game " + value;
        return value;
    }

    private static ConcurrentDictionary<Guid, string> Map(ApplicationPaths paths) => Cache.GetOrAdd(paths.Root, _ => new());

    private static Guid? IdOf(string folder)
    {
        var name = Path.GetFileName(folder);
        if (Guid.TryParseExact(name, "N", out var legacy)) return legacy == Guid.Empty ? null : legacy;
        try
        {
            var marker = Path.Combine(folder, MarkerFile);
            if (File.Exists(marker) && new FileInfo(marker).Length < 256 && Guid.TryParse(File.ReadAllText(marker).Trim(), out var marked) && marked != Guid.Empty) return marked;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        foreach (var file in new[] { "profile.json", "profile.json.bak" })
        {
            try
            {
                var path = Path.Combine(folder, file);
                var info = new FileInfo(path);
                if (!info.Exists || info.Length > MaxProfileJsonBytes) continue;
                using var document = JsonDocument.Parse(File.ReadAllBytes(path));
                if (document.RootElement.ValueKind == JsonValueKind.Object &&
                    document.RootElement.TryGetProperty("id", out var property) &&
                    property.ValueKind == JsonValueKind.String &&
                    Guid.TryParse(property.GetString(), out var id) && id != Guid.Empty)
                    return id;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { }
        }
        return null;
    }
}
