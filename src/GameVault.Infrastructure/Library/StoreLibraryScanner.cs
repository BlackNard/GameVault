using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using GameVault.Infrastructure.Launching;

namespace GameVault.Infrastructure.Library;

/// <summary>A game found in a local Steam or Epic installation. Nothing is read from the network and no account is used (ticket #85).</summary>
public sealed record InstalledStoreGame(StoreProvider Provider, string StoreId, string Name, string InstallRoot, string? ExecutablePath, string[] Candidates, long? SizeBytes);

public enum StoreGameStatus { New, InLibrary, Removed }

public sealed record LibraryExecutable(Guid GameId, string ExecutablePath, bool IsArchived);

public sealed record StoreScanResult(InstalledStoreGame[] Games, string[] Warnings);

/// <summary>
/// Lists installed Steam and Epic games from the same local metadata GameVault already trusts for launching: Steam appmanifest_*.acf in every
/// library folder (libraryfolders.vdf) and Epic *.item manifests. Redistributables and tools are left out.
/// </summary>
public sealed class StoreLibraryScanner(string? steamRoot, string? epicManifestDirectory = null)
{
    private const int MaxFilesPerFolder = 4000, MaxFileBytes = 512 * 1024, MaxCandidates = 12;
    private readonly string epicManifests = epicManifestDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Epic", "EpicGamesLauncher", "Data", "Manifests");

    private static readonly Regex ToolName = new(@"^(Steamworks|Steam Linux Runtime|Proton|Steam Controller|SteamVR Performance)|Redistributable|\bRedist\b|Common Redistributables|Dedicated Server Tool|Source SDK Base|Wallpaper Engine Tools",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly HashSet<string> ToolSteamIds = ["228980", "1007", "1070560", "1391110", "1628350", "1493710", "2180100", "1826330", "1887720", "250820"];
    private static readonly Regex NonGameExecutable = new(@"^(unins\d*|uninstall|setup|vc_?redist.*|vcredist.*|dxsetup|dotnet.*|crashreport.*|crashpad.*|unitycrashhandler.*|ue4prereqsetup.*|ueprereqsetup.*|oalinst|physx.*|launcher_helper|eosoverlay.*|easyanticheat.*|epicwebhelper|cefsubprocess|7z|notification_helper)\.exe$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static bool IsToolName(string name) => ToolName.IsMatch(name);

    public StoreScanResult Scan()
    {
        var games = new List<InstalledStoreGame>();
        var warnings = new List<string>();
        foreach (var steamapps in SteamLibraries(warnings))
            foreach (var game in ScanSteam(steamapps, warnings)) games.Add(game);
        foreach (var game in ScanEpic(warnings)) games.Add(game);
        var unique = games.GroupBy(g => g.Provider + ":" + g.StoreId, StringComparer.OrdinalIgnoreCase).Select(g => g.First())
            .OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase).ThenBy(g => g.Provider).ToArray();
        return new(unique, [.. warnings.Distinct()]);
    }

    // ---------- Steam ----------
    private IEnumerable<string> SteamLibraries(List<string> warnings)
    {
        var folders = new List<string>();
        if (string.IsNullOrWhiteSpace(steamRoot) || !Directory.Exists(steamRoot)) return folders;
        var main = Path.Combine(steamRoot, "steamapps");
        if (Directory.Exists(main)) folders.Add(main);
        var file = Path.Combine(main, "libraryfolders.vdf");
        try
        {
            if (File.Exists(file) && Vdf.Parse(ReadText(file)) is { } root && root.Child("libraryfolders") is { } list)
                foreach (var (_, entry) in list.Children)
                    if (entry.Text("path") is { } path && Path.IsPathFullyQualified(path))
                    {
                        var steamapps = Path.Combine(Path.GetFullPath(path), "steamapps");
                        if (Directory.Exists(steamapps)) folders.Add(steamapps);
                    }
        }
        catch (Exception error) when (IsMetadataError(error)) { warnings.Add("Steam's list of library folders could not be read; only the main Steam library was checked."); }
        return folders.Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static IEnumerable<InstalledStoreGame> ScanSteam(string steamapps, List<string> warnings)
    {
        string[] files;
        try { files = [.. Directory.EnumerateFiles(steamapps, "appmanifest_*.acf", SearchOption.TopDirectoryOnly).Take(MaxFilesPerFolder)]; }
        catch (Exception error) when (IsMetadataError(error)) { warnings.Add("A Steam library folder could not be read."); yield break; }
        var common = Path.Combine(steamapps, "common");
        foreach (var file in files)
        {
            InstalledStoreGame? game = null;
            try
            {
                var state = Vdf.Parse(ReadText(file))?.Child("AppState");
                if (state is null) continue;
                var id = state.Text("appid");
                var name = state.Text("name");
                var install = state.Text("installdir");
                if (id is null || !id.All(char.IsAsciiDigit) || id.Length is 0 or > 10 || string.IsNullOrWhiteSpace(name) || ToolSteamIds.Contains(id) || IsToolName(name)) continue;
                if (!Path.GetFileName(file).Equals($"appmanifest_{id}.acf", StringComparison.OrdinalIgnoreCase)) continue;
                if (install is null || install is "." or ".." || install.IndexOfAny(['/', '\\', ':']) >= 0 || install.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) continue;
                // StateFlags bit 4 means fully installed; updates in progress still have the game on disk.
                if (state.Text("StateFlags") is { } flags && int.TryParse(flags, out var value) && (value & 4) == 0 && (value & 256) == 0) continue;
                var root = Path.GetFullPath(Path.Combine(common, install));
                if (!Directory.Exists(root)) continue;
                var (best, candidates) = Executables(root, name, install);
                long? size = state.Text("SizeOnDisk") is { } s && long.TryParse(s, out var bytes) && bytes > 0 ? bytes : null;
                game = new(StoreProvider.Steam, id, name.Trim(), root, best, candidates, size);
            }
            catch (Exception error) when (IsMetadataError(error)) { warnings.Add("A Steam game's information could not be read and was skipped."); }
            if (game is not null) yield return game;
        }
    }

    // ---------- Epic ----------
    private IEnumerable<InstalledStoreGame> ScanEpic(List<string> warnings)
    {
        if (!Directory.Exists(epicManifests)) yield break;
        string[] files;
        try { files = [.. Directory.EnumerateFiles(epicManifests, "*.item", SearchOption.TopDirectoryOnly).Take(MaxFilesPerFolder)]; }
        catch (Exception error) when (IsMetadataError(error)) { warnings.Add("Epic's game list could not be read."); yield break; }
        foreach (var file in files)
        {
            InstalledStoreGame? game = null;
            try
            {
                using var document = JsonDocument.Parse(ReadText(file), new JsonDocumentOptions { MaxDepth = 16 });
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object) continue;
                string? Text(string name) => root.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
                var appName = Text("AppName");
                var display = Text("DisplayName");
                var install = Text("InstallLocation");
                var launch = Text("LaunchExecutable");
                if (string.IsNullOrWhiteSpace(appName) || string.IsNullOrWhiteSpace(display) || install is null || !Path.IsPathFullyQualified(install) || IsToolName(display)) continue;
                // Add-ons and DLC point at their main game.
                if (Text("MainGameAppName") is { Length: > 0 } main && !main.Equals(appName, StringComparison.OrdinalIgnoreCase)) continue;
                if (root.TryGetProperty("bIsIncompleteInstall", out var incomplete) && incomplete.ValueKind == JsonValueKind.True) continue;
                var installRoot = Path.GetFullPath(install);
                if (!Directory.Exists(installRoot)) continue;
                var (best, candidates) = Executables(installRoot, display, Path.GetFileName(installRoot));
                if (!string.IsNullOrWhiteSpace(launch) && !Path.IsPathRooted(launch))
                {
                    var exe = Path.GetFullPath(Path.Combine(installRoot, launch));
                    if (Inside(exe, installRoot) && exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(exe))
                    {
                        best = exe;
                        candidates = [exe, .. candidates.Where(c => !c.Equals(exe, StringComparison.OrdinalIgnoreCase))];
                    }
                }
                long? size = root.TryGetProperty("InstallSize", out var s) && s.ValueKind == JsonValueKind.Number && s.TryGetInt64(out var bytes) && bytes > 0 ? bytes : null;
                game = new(StoreProvider.Epic, appName, display.Trim(), installRoot, best, [.. candidates.Take(MaxCandidates)], size);
            }
            catch (Exception error) when (IsMetadataError(error)) { warnings.Add("An Epic game's information could not be read and was skipped."); }
            if (game is not null) yield return game;
        }
    }

    // ---------- executables ----------
    /// <summary>
    /// Finds the likely game executable: top-level programs first (without installers, crash handlers and redistributables), then Unreal's
    /// Binaries/Win64/*-Win64-Shipping.exe. Several programs are ranked by how well they match the game or folder name, then by size.
    /// </summary>
    public static (string? Best, string[] Candidates) Executables(string installRoot, string gameName, string folderName)
    {
        var found = new List<string>();
        try
        {
            found.AddRange(Directory.EnumerateFiles(installRoot, "*.exe", SearchOption.TopDirectoryOnly).Where(f => !NonGameExecutable.IsMatch(Path.GetFileName(f))).Take(64));
            foreach (var child in Directory.EnumerateDirectories(installRoot).Take(64))
            {
                if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0) continue;
                var win64 = Path.Combine(child, "Binaries", "Win64");
                if (Directory.Exists(win64) && (File.GetAttributes(win64) & FileAttributes.ReparsePoint) == 0)
                    found.AddRange(Directory.EnumerateFiles(win64, "*-Win64-Shipping.exe").Take(4));
            }
            var shipping = Path.Combine(installRoot, "Binaries", "Win64");
            if (Directory.Exists(shipping)) found.AddRange(Directory.EnumerateFiles(shipping, "*.exe").Where(f => !NonGameExecutable.IsMatch(Path.GetFileName(f))).Take(8));
        }
        catch (Exception error) when (IsMetadataError(error)) { }
        var ranked = found.Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(f => Score(f, gameName, folderName))
            .ThenByDescending(f => { try { return new FileInfo(f).Length; } catch (IOException) { return 0L; } })
            .Take(MaxCandidates).ToArray();
        // A single obvious program is chosen automatically; otherwise the best match is only a suggestion the owner can change.
        return (ranked.FirstOrDefault(), ranked);
    }

    private static int Score(string path, string gameName, string folderName)
    {
        var file = Normalize(Path.GetFileNameWithoutExtension(path));
        if (file.Length == 0) return 0;
        var score = 0;
        foreach (var name in new[] { gameName, folderName })
        {
            var n = Normalize(name);
            if (n.Length == 0) continue;
            if (n == file) score += 100;
            else if (n.Contains(file) || file.Contains(n)) score += 60;
        }
        if (path.EndsWith("-Win64-Shipping.exe", StringComparison.OrdinalIgnoreCase)) score += 5;
        if (Path.GetFileName(path).Contains("launcher", StringComparison.OrdinalIgnoreCase)) score -= 10;
        return score;
    }

    private static string Normalize(string text) => new(text.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    // ---------- comparing with the library ----------
    /// <summary>A game counts as already added when any library executable lives inside its install folder.</summary>
    public static (StoreGameStatus Status, Guid? GameId) Classify(InstalledStoreGame game, IEnumerable<LibraryExecutable> library)
    {
        var inside = library.Where(l => Inside(l.ExecutablePath, game.InstallRoot) ||
            (game.ExecutablePath is not null && string.Equals(Path.GetFullPath(l.ExecutablePath), Path.GetFullPath(game.ExecutablePath), StringComparison.OrdinalIgnoreCase))).ToArray();
        if (inside.Any(l => !l.IsArchived)) return (StoreGameStatus.InLibrary, inside.First(l => !l.IsArchived).GameId);
        if (inside.Length > 0) return (StoreGameStatus.Removed, inside[0].GameId);
        return (StoreGameStatus.New, null);
    }

    private static bool Inside(string path, string root)
    {
        try { return Path.GetFullPath(path).StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase); }
        catch (ArgumentException) { return false; }
    }

    private static bool IsMetadataError(Exception error) => error is IOException or UnauthorizedAccessException or ArgumentException or JsonException or InvalidOperationException or FormatException or DecoderFallbackException;

    private static string ReadText(string file)
    {
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (stream.Length > MaxFileBytes) throw new IOException("Store metadata is too large.");
        var bytes = new byte[stream.Length];
        stream.ReadExactly(bytes);
        return new UTF8Encoding(false, true).GetString(bytes).TrimStart('﻿');
    }
}

/// <summary>A small strict reader for Valve's text format (KeyValues): nested objects of quoted keys and values.</summary>
internal sealed class Vdf
{
    private const int MaxDepth = 16;
    private readonly Dictionary<string, Vdf> children = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> values = new(StringComparer.OrdinalIgnoreCase);

    public IEnumerable<KeyValuePair<string, Vdf>> Children => children;
    public Vdf? Child(string key) => children.GetValueOrDefault(key);
    public string? Text(string key) => values.GetValueOrDefault(key);

    public static Vdf? Parse(string text)
    {
        var position = 0;
        (char Kind, string Value) Next()
        {
            while (position < text.Length)
            {
                if (char.IsWhiteSpace(text[position])) { position++; continue; }
                if (text[position] == '/' && position + 1 < text.Length && text[position + 1] == '/') { while (position < text.Length && text[position] != '\n') position++; continue; }
                break;
            }
            if (position == text.Length) return ('\0', "");
            if (text[position] is '{' or '}') return (text[position++], "");
            if (text[position++] != '"') throw new FormatException();
            var result = new StringBuilder();
            while (position < text.Length)
            {
                var c = text[position++];
                if (c == '"') return ('s', result.ToString());
                if (c == '\\') { if (position == text.Length) throw new FormatException(); c = text[position++] switch { 'n' => '\n', 't' => '\t', var other => other }; }
                result.Append(c);
            }
            throw new FormatException();
        }
        Vdf Object(int depth, bool top)
        {
            if (depth > MaxDepth) throw new FormatException();
            var node = new Vdf();
            while (true)
            {
                var key = Next();
                if (key.Kind == '}') { if (top) throw new FormatException(); return node; }
                if (key.Kind == '\0') { if (!top) throw new FormatException(); return node; }
                if (key.Kind != 's') throw new FormatException();
                var value = Next();
                if (value.Kind == '{') node.children[key.Value] = Object(depth + 1, false);
                else if (value.Kind == 's') node.values[key.Value] = value.Value;
                else throw new FormatException();
            }
        }
        return Object(0, true);
    }
}
