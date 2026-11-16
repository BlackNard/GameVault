using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace GameVault.Infrastructure.Launching;

public enum StoreProvider { Steam, Epic }
public sealed record StoreLaunchPlan(StoreProvider Provider, string StoreKey, string ActivationUri, string InstallRoot, string TargetExecutablePath);
public interface IStoreGameLauncher
{
    StoreLaunchPlan? Resolve(string selectedExecutable, string? targetOverride);
    void RequestLaunch(StoreLaunchPlan plan);
}

/// <summary>Reads installation metadata only; store clients retain authentication and DRM responsibility.</summary>
public sealed class StoreGameLauncher : IStoreGameLauncher
{
    private const int MaxFileBytes = 256 * 1024, MaxFiles = 256, MaxTotalBytes = 8 * 1024 * 1024;
    private readonly string epicManifests;
    private readonly Action<string> activate;
    public StoreGameLauncher(string? epicManifestDirectory = null, Action<string>? activate = null)
    {
        epicManifests = epicManifestDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Epic", "EpicGamesLauncher", "Data", "Manifests");
        this.activate = activate ?? (uri => { using var process = Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true }); });
    }

    public StoreLaunchPlan? Resolve(string selectedExecutable, string? targetOverride)
    {
        var selected = Path.GetFullPath(selectedExecutable);
        var matches = new List<(StoreProvider Provider, string Key, string Root, string? Launch)>();
        // Only the selected executable's own Steam library is inspected.
        var directory = Directory.GetParent(selected);
        for (var depth = 0; directory is not null && depth < 16; depth++, directory = directory.Parent)
        {
            if (!directory.Name.Equals("steamapps", StringComparison.OrdinalIgnoreCase)) continue;
            var common = Path.Combine(directory.FullName, "common");
            if (!Inside(selected, common)) break;
            foreach (var file in MetadataFiles(directory.FullName, "appmanifest_*.acf"))
            {
                try
                {
                    var state = ReadSteamState(ReadMetadata(file));
                    if (!state.TryGetValue("appid", out var id) || !NumericId(id) || !Path.GetFileName(file).Equals($"appmanifest_{id}.acf", StringComparison.OrdinalIgnoreCase) || !state.TryGetValue("installdir", out var install) || !SimpleDirectory(install)) continue;
                    var installRoot = Path.GetFullPath(Path.Combine(common, install));
                    if (Inside(selected, installRoot)) matches.Add((StoreProvider.Steam, "Steam:" + id, installRoot, null));
                }
                catch (Exception error) when (MetadataError(error)) { }
            }
            break;
        }
        foreach (var file in MetadataFiles(epicManifests, "*.item"))
        {
            try
            {
                using var document = JsonDocument.Parse(ReadMetadata(file), new JsonDocumentOptions { MaxDepth = 16 });
                var fields = document.RootElement.EnumerateObject().ToArray();
                if (fields.Count(p => p.NameEquals("InstallLocation")) != 1 || fields.Count(p => p.NameEquals("LaunchExecutable")) != 1) continue;
                var install = document.RootElement.GetProperty("InstallLocation").GetString();
                var executable = document.RootElement.GetProperty("LaunchExecutable").GetString();
                if (install is null || !Path.IsPathFullyQualified(install) || string.IsNullOrWhiteSpace(executable) || Path.IsPathRooted(executable)) continue;
                var installRoot = Path.GetFullPath(install); var launch = Path.GetFullPath(Path.Combine(installRoot, executable));
                if (Inside(selected, installRoot) && Inside(launch, installRoot) && IsExecutable(launch)) matches.Add((StoreProvider.Epic, EpicKey(installRoot), installRoot, launch));
            }
            catch (Exception error) when (MetadataError(error)) { }
        }
        if (matches.Count > 1) throw new IOException("Multiple store installations match this executable. Repair the local installation metadata before playing.");
        if (matches.Count == 0) return null;
        var match = matches[0];
        var target = targetOverride is null ? ResolveTarget(selected, match.Root, match.Launch) : Path.GetFullPath(targetOverride);
        if (!IsExecutable(target)) throw new IOException("The actual game executable is missing. Choose it in Launch settings.");
        return new(match.Provider, match.Key, Activation(match.Provider, match.Key, match.Root), match.Root, target);
    }

    public void RequestLaunch(StoreLaunchPlan plan)
    {
        if (!Path.IsPathFullyQualified(plan.InstallRoot) || !IsExecutable(plan.TargetExecutablePath) ||
            !string.Equals(plan.ActivationUri, Activation(plan.Provider, plan.StoreKey, plan.InstallRoot), StringComparison.Ordinal))
            throw new ArgumentException("The store launch request is invalid.", nameof(plan));
        activate(plan.ActivationUri);
    }

    private static string Activation(StoreProvider provider, string key, string root)
    {
        if (provider == StoreProvider.Steam && key.StartsWith("Steam:", StringComparison.Ordinal) && NumericId(key[6..])) return "steam://run/" + key[6..];
        if (provider == StoreProvider.Epic && key == EpicKey(root)) return "com.epicgames.launcher://apps/" + Uri.EscapeDataString(Path.GetFullPath(root)) + "?action=launch&silent=true";
        throw new ArgumentException("The store identity is invalid.");
    }
    private static string EpicKey(string root) => "Epic:" + Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar).ToUpperInvariant();
    private static bool NumericId(string id) => id.Length is > 0 and <= 10 && id.All(char.IsAsciiDigit) && uint.TryParse(id, out var number) && number > 0;
    private static bool SimpleDirectory(string value) => !string.IsNullOrWhiteSpace(value) && value is not "." and not ".." && value.IndexOfAny(['/', '\\', ':']) < 0 && value.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;
    private static bool Inside(string path, string root) => Path.GetFullPath(path).StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    private static bool IsExecutable(string path) => Path.IsPathFullyQualified(path) && Path.GetExtension(path).Equals(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(path);
    private static bool MetadataError(Exception error) => error is IOException or UnauthorizedAccessException or ArgumentException or JsonException or InvalidOperationException or FormatException;
    private static IEnumerable<string> MetadataFiles(string directory, string pattern)
    {
        if (!Directory.Exists(directory)) return [];
        try
        {
            var files = Directory.EnumerateFiles(directory, pattern, SearchOption.TopDirectoryOnly).Take(MaxFiles + 1).ToArray();
            if (files.Length > MaxFiles || files.Sum(file => new FileInfo(file).Length) > MaxTotalBytes) throw new IOException("Store installation metadata exceeds the safe inspection limit.");
            return files;
        }
        catch (UnauthorizedAccessException error) { throw new IOException("Store installation metadata could not be read safely.", error); }
    }
    private static string ReadMetadata(string file)
    {
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (stream.Length > MaxFileBytes) throw new IOException("Store metadata is too large.");
        var bytes = new byte[MaxFileBytes + 1]; var length = 0;
        while (length < bytes.Length) { var read = stream.Read(bytes, length, bytes.Length - length); if (read == 0) break; length += read; }
        if (length > MaxFileBytes) throw new IOException("Store metadata is too large.");
        return new UTF8Encoding(false, true).GetString(bytes, 0, length).TrimStart('\uFEFF');
    }
    private static Dictionary<string, string> ReadSteamState(string text)
    {
        var position = 0;
        (char Kind, string Value) Next()
        {
            while (position < text.Length && char.IsWhiteSpace(text[position])) position++;
            if (position == text.Length) return ('\0', "");
            if (text[position] is '{' or '}') return (text[position++], "");
            if (text[position++] != '"') throw new FormatException();
            var result = new StringBuilder();
            while (position < text.Length)
            {
                var c = text[position++]; if (c == '"') return ('s', result.ToString());
                if (c == '\\') { if (position == text.Length) throw new FormatException(); c = text[position++]; }
                result.Append(c);
            }
            throw new FormatException();
        }
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        void Object(int depth)
        {
            if (depth > 16) throw new FormatException();
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            while (true)
            {
                var key = Next(); if (key.Kind == '}') return;
                if (key.Kind != 's' || key.Value.Length == 0 || !keys.Add(key.Value)) throw new FormatException();
                var value = Next(); if (value.Kind == '{') Object(depth + 1);
                else { if (value.Kind != 's') throw new FormatException(); if (depth == 1) values.Add(key.Value, value.Value); }
            }
        }
        if (Next() != ('s', "AppState") || Next().Kind != '{') throw new FormatException(); Object(1);
        if (Next().Kind != '\0') throw new FormatException(); return values;
    }
    private static string ResolveTarget(string selected, string root, string? metadataLaunch)
    {
        if (!string.Equals(Path.GetDirectoryName(selected), root, StringComparison.OrdinalIgnoreCase)) return selected;
        var candidates = new List<string>();
        var roots = new[] { root }.Concat(Directory.EnumerateDirectories(root).Take(MaxFiles + 1)).ToArray();
        if (roots.Length > MaxFiles + 1) throw new IOException("Choose the actual game executable in Launch settings; the installation has too many directories to inspect safely.");
        foreach (var candidateRoot in roots)
        {
            // Do not follow junctions while discovering an Unreal bootstrapper's actual executable.
            if ((File.GetAttributes(candidateRoot) & FileAttributes.ReparsePoint) != 0) continue;
            var binaries = Path.Combine(candidateRoot, "Binaries"); var win64 = Path.Combine(binaries, "Win64");
            if (!Directory.Exists(win64) || (File.GetAttributes(binaries) & FileAttributes.ReparsePoint) != 0 || (File.GetAttributes(win64) & FileAttributes.ReparsePoint) != 0) continue;
            candidates.AddRange(Directory.EnumerateFiles(win64, "*-Win64-Shipping.exe").Take(2));
            if (candidates.Count > 1) throw new IOException("More than one actual game executable was found. Choose the correct target in Launch settings.");
        }
        return candidates.Count == 1 ? candidates[0] : metadataLaunch ?? selected;
    }
}
