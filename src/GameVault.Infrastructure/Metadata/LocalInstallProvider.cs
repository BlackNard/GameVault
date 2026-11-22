using GameVault.Core;
using GameVault.Infrastructure.Library;

namespace GameVault.Infrastructure.Metadata;

/// <summary>Install size from the files on this PC. Steam games read SizeOnDisk from their own appmanifest; everything else sums the install folder.</summary>
public sealed class LocalInstallProvider : IGameMetadataProvider
{
    public const string ProviderId = "local";
    private const int ClusterBytes = 4096;
    private const int MaxFiles = 400_000;

    public string Id => ProviderId;
    public string DisplayName => "This PC";
    public IReadOnlySet<MetadataField> AvailableFields { get; } = new HashSet<MetadataField> { MetadataField.InstallSize, MetadataField.Installed };

    public Task<ProviderData?> GetAsync(MetadataRequest request, CancellationToken cancellationToken) => Task.Run<ProviderData?>(() =>
    {
        // What the launcher recorded about this install (build, last update, language, version). Cheap, so it is read every time.
        var installed = InstalledInfo(request);
        long? size = null;
        if (request.InstallRoot is { } root && Directory.Exists(root))
        {
            // An unchanged folder keeps its previous size (the caller keeps the old value when nothing is returned).
            if (!request.ForceSizeScan && request.LastSizeScan is { } last && Directory.GetLastWriteTimeUtc(root) <= last.UtcDateTime) { }
            else if (request.SteamAppId is { } id && request.SteamAppsFolder is { } steamapps && SteamSize(steamapps, id) is long steamSize) size = steamSize;
            else size = Scan(root, request.SizeOnDisk, cancellationToken);
        }
        return size is null && installed is null ? null : new ProviderData(InstallSizeBytes: size, Installed: installed);
    }, cancellationToken);

    /// <summary>Steam: build, last update, language and beta branch from the app manifest. Epic: the installed version from its manifest. Null when there is nothing.</summary>
    public static string[]? InstalledInfo(MetadataRequest request)
    {
        var lines = new List<string>();
        try
        {
            if (request.SteamAppId is { } id && request.SteamAppsFolder is { } steamapps)
            {
                var file = Path.Combine(steamapps, $"appmanifest_{id}.acf");
                if (File.Exists(file) && new FileInfo(file).Length <= 512 * 1024 && Vdf.Parse(File.ReadAllText(file))?.Child("AppState") is { } state)
                {
                    if (state.Text("buildid") is { Length: > 0 and <= 12 } build && build.All(char.IsAsciiDigit) && build != "0") lines.Add("Build " + build);
                    if (state.Text("LastUpdated") is { } updated && long.TryParse(updated, out var seconds) && seconds > 0 && seconds < 4_102_444_800)
                        lines.Add("Updated " + DateTimeOffset.FromUnixTimeSeconds(seconds).ToLocalTime().ToString("d MMM yyyy", System.Globalization.CultureInfo.InvariantCulture));
                    if (state.Child("UserConfig")?.Text("language") is { Length: > 0 and <= 30 } language && language.All(char.IsLetter)) lines.Add("Language: " + language);
                    if (state.Child("UserConfig")?.Text("BetaKey") is { Length: > 0 and <= 40 } branch && branch.All(c => char.IsLetterOrDigit(c) || c is '_' or '-')) lines.Add("Branch: " + branch);
                }
            }
            if (EpicLocalInfo.Find(request.ExecutablePath) is { Version: { Length: > 0 and <= 60 } version }) lines.Add("Epic version " + version);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or FormatException or ArgumentException) { }
        return lines.Count > 0 ? [.. lines] : null;
    }

    public static long? SteamSize(string steamappsFolder, string appId)
    {
        try
        {
            var file = Path.Combine(steamappsFolder, $"appmanifest_{appId}.acf");
            if (!File.Exists(file) || new FileInfo(file).Length > 512 * 1024) return null;
            var state = Vdf.Parse(File.ReadAllText(file))?.Child("AppState");
            return state?.Text("SizeOnDisk") is { } text && long.TryParse(text, out var bytes) && bytes > 0 ? bytes : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or FormatException) { return null; }
    }

    /// <summary>Total size of the files under a folder, not following links. With sizeOnDisk each file counts in whole 4 KB clusters. Null when the folder is too large to scan safely.</summary>
    public static long? Scan(string root, bool sizeOnDisk, CancellationToken cancellationToken)
    {
        long total = 0;
        var files = 0;
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var folder = pending.Pop();
            try
            {
                foreach (var entry in new DirectoryInfo(folder).EnumerateFileSystemInfos())
                {
                    if ((entry.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                    if (entry is DirectoryInfo directory) { pending.Push(directory.FullName); continue; }
                    if (entry is not FileInfo file) continue;
                    if (++files > MaxFiles) return null;
                    total += sizeOnDisk ? (file.Length + ClusterBytes - 1) / ClusterBytes * ClusterBytes : file.Length;
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
        return total;
    }
}
