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
    public IReadOnlySet<MetadataField> AvailableFields { get; } = new HashSet<MetadataField> { MetadataField.InstallSize };

    public Task<ProviderData?> GetAsync(MetadataRequest request, CancellationToken cancellationToken) => Task.Run<ProviderData?>(() =>
    {
        if (request.InstallRoot is not { } root || !Directory.Exists(root)) return null;
        // An unchanged folder keeps its previous size (the caller keeps the old value when nothing is returned).
        if (!request.ForceSizeScan && request.LastSizeScan is { } last && Directory.GetLastWriteTimeUtc(root) <= last.UtcDateTime) return null;
        if (request.SteamAppId is { } id && request.SteamAppsFolder is { } steamapps && SteamSize(steamapps, id) is long steamSize) return new ProviderData(InstallSizeBytes: steamSize);
        var size = Scan(root, request.SizeOnDisk, cancellationToken);
        return size is null ? null : new ProviderData(InstallSizeBytes: size);
    }, cancellationToken);

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
