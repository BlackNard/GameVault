using System.Diagnostics;
using System.Text.Json;
using GameVault.Core;

namespace GameVault.Infrastructure.Metadata;

public sealed record EpicInstallInfo(string Namespace, string ItemId, string AppName, string DisplayName, string? Version);

/// <summary>What the Epic Games launcher recorded about an installed game (its local manifest). Read only, bounded, never sent anywhere.</summary>
public static class EpicLocalInfo
{
    private const int MaxFiles = 512, MaxBytes = 512 * 1024;

    public static string DefaultManifestFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Epic", "EpicGamesLauncher", "Data", "Manifests");

    public static EpicInstallInfo? Find(string executablePath, string? manifestFolder = null)
    {
        try
        {
            var folder = manifestFolder ?? DefaultManifestFolder;
            if (!Directory.Exists(folder) || string.IsNullOrWhiteSpace(executablePath)) return null;
            var exe = Path.GetFullPath(executablePath);
            foreach (var file in Directory.EnumerateFiles(folder, "*.item").Take(MaxFiles))
            {
                if (new FileInfo(file).Length > MaxBytes) continue;
                try
                {
                    using var document = JsonDocument.Parse(File.ReadAllText(file), new JsonDocumentOptions { MaxDepth = 16 });
                    var root = document.RootElement;
                    if (root.ValueKind != JsonValueKind.Object) continue;
                    var install = Text(root, "InstallLocation");
                    var ns = Text(root, "CatalogNamespace");
                    if (install is null || ns is null || !Path.IsPathFullyQualified(install) || ns.Length > 64 || !ns.All(char.IsLetterOrDigit)) continue;
                    if (Text(root, "MainGameAppName") is { Length: > 0 } main && Text(root, "AppName") is { } app && !main.Equals(app, StringComparison.OrdinalIgnoreCase)) continue;
                    var prefix = Path.GetFullPath(install).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                    if (!exe.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                    return new(ns, Text(root, "CatalogItemId") ?? "", Text(root, "AppName") ?? "", Text(root, "DisplayName") ?? "", Text(root, "AppVersionString"));
                }
                catch (Exception error) when (error is JsonException or IOException or UnauthorizedAccessException or ArgumentException) { }
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException) { }
        return null;
    }

    private static string? Text(JsonElement root, string name) => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}

public sealed record GogInstallInfo(string GameId, string? Name);

/// <summary>GOG games carry a <c>goggame-&lt;id&gt;.info</c> file in their install folder. Read only and bounded.</summary>
public static class GogLocalInfo
{
    public static GogInstallInfo? Find(string executablePath)
    {
        try
        {
            var folder = Path.GetDirectoryName(Path.GetFullPath(executablePath));
            for (var depth = 0; folder is not null && depth < 4; depth++, folder = Path.GetDirectoryName(folder))
            {
                foreach (var file in Directory.EnumerateFiles(folder, "goggame-*.info").Take(4))
                {
                    if (new FileInfo(file).Length > 256 * 1024) continue;
                    try
                    {
                        using var document = JsonDocument.Parse(File.ReadAllText(file), new JsonDocumentOptions { MaxDepth = 16 });
                        var root = document.RootElement;
                        if (root.ValueKind != JsonValueKind.Object) continue;
                        var id = root.TryGetProperty("gameId", out var g) ? (g.ValueKind == JsonValueKind.String ? g.GetString() : g.ValueKind == JsonValueKind.Number ? g.GetRawText() : null) : null;
                        if (id is { Length: > 0 and <= 16 } && id.All(char.IsAsciiDigit))
                            return new(id, root.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null);
                    }
                    catch (Exception error) when (error is JsonException or IOException or UnauthorizedAccessException) { }
                }
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException) { }
        return null;
    }
}

/// <summary>
/// The developer from the game's own executable (its version resource). No network. Engine and runtime companies are ignored because they are
/// not the developer. For a store game this is only a last resort after the store's own data.
/// </summary>
public sealed class LocalFileProvider : IGameMetadataProvider
{
    public const string ProviderId = "localfile";
    private static readonly string[] NotDevelopers = ["epic games", "unity technologies", "microsoft", "valve corporation", "the qt company", "intel corporation", "nvidia"];

    public string Id => ProviderId;
    public string DisplayName => "The game file";
    public IReadOnlySet<MetadataField> AvailableFields { get; } = new HashSet<MetadataField> { MetadataField.Developers };

    public Task<ProviderData?> GetAsync(MetadataRequest request, CancellationToken cancellationToken) => Task.Run<ProviderData?>(() =>
    {
        try
        {
            if (!File.Exists(request.ExecutablePath)) return null;
            var company = FileVersionInfo.GetVersionInfo(request.ExecutablePath).CompanyName?.Trim();
            return Plausible(company) ? new ProviderData(Developers: [company!]) : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException) { return null; }
    }, cancellationToken);

    public static bool Plausible(string? company) => company is { Length: >= 2 and <= 80 } && company.All(c => !char.IsControl(c)) &&
        !NotDevelopers.Any(n => company.Contains(n, StringComparison.OrdinalIgnoreCase)) && company.Any(char.IsLetter);
}
