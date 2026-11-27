using System.Diagnostics;
using System.Text.Json;
using GameVault.Core;
using GameVault.Infrastructure.Launching;

namespace GameVault.Infrastructure.Saves;

/// <summary>Store numbers and publisher names read from a game's own folder. Only those values are taken; they are never logged.</summary>
public sealed record GameSaveIdentity(string? SteamAppId, string? GogId, string? EpicApp, string? Company, string? Product, IReadOnlySet<string> Tokens)
{
    private const int MaxFileBytes = 64 * 1024, MaxSubfolders = 200;
    private static readonly string[] SettingsFiles = ["hlm.ini", "steam_emu.ini", "cream_api.ini"];
    // Program folders inside an install; the identity files usually sit above them.
    private static readonly string[] ProgramFolders = ["binaries", "bin", "win64", "win32", "x64", "x86"];

    public static GameSaveIdentity Read(GameProfile profile)
    {
        var folder = Path.GetDirectoryName(profile.ExecutablePath) ?? "";
        string? storeId = null, installRoot = null;
        try
        {
            var plan = new StoreGameLauncher().Resolve(profile.ExecutablePath, null);
            if (plan is not null) { installRoot = plan.InstallRoot; if (plan.StoreKey.StartsWith("Steam:", StringComparison.Ordinal)) storeId = plan.StoreKey[6..]; }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException) { }
        if (installRoot is null)
        {
            installRoot = folder;
            for (var i = 0; i < 3 && ProgramFolders.Contains(Path.GetFileName(installRoot), StringComparer.OrdinalIgnoreCase) && Path.GetDirectoryName(installRoot) is { Length: > 3 } parent; i++)
                installRoot = parent;
        }

        var folders = new List<string>();
        foreach (var start in new[] { folder, installRoot }.Where(f => f.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            folders.Add(start);
            try { folders.AddRange(new DirectoryInfo(start).EnumerateDirectories().Take(MaxSubfolders).Where(d => (d.Attributes & FileAttributes.ReparsePoint) == 0).Select(d => d.FullName)); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException) { }
        }

        string? steam = null, gog = null, epic = null;
        foreach (var place in folders.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            steam ??= Number(ReadSmall(Path.Combine(place, "steam_appid.txt")));
            foreach (var file in SettingsFiles) steam ??= AppIdLine(ReadSmall(Path.Combine(place, file)));
            try
            {
                if (!Directory.Exists(place)) continue;
                gog ??= Directory.EnumerateFiles(place, "goggame-*.info").Take(4).Select(f => Number(Path.GetFileNameWithoutExtension(f)["goggame-".Length..])).FirstOrDefault(n => n is not null);
                if (Path.GetFileName(place).Equals(".egstore", StringComparison.OrdinalIgnoreCase))
                    foreach (var manifest in Directory.EnumerateFiles(place, "*.mancpn").Take(4)) epic ??= EpicAppName(ReadSmall(manifest));
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
        steam ??= Number(storeId);

        string? company = null, product = null;
        try
        {
            if (File.Exists(profile.ExecutablePath))
            {
                var version = FileVersionInfo.GetVersionInfo(profile.ExecutablePath);
                company = FolderName(version.CompanyName);
                product = FolderName(version.ProductName);
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException) { }

        var tokens = GameNameTokens.For([profile.Name, Path.GetFileName(folder), Path.GetFileName(installRoot), Path.GetFileNameWithoutExtension(profile.ExecutablePath), product, steam]);
        return new(steam, gog, epic, company, product, tokens);
    }

    private static string? ReadSmall(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length > MaxFileBytes || (info.Attributes & FileAttributes.ReparsePoint) != 0) return null;
            return File.ReadAllText(path);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException) { return null; }
    }

    private static string? Number(string? value)
    {
        value = value?.Trim().TrimEnd('\0');
        return value is { Length: > 0 and <= 12 } && value.All(char.IsAsciiDigit) && value.Any(c => c != '0') ? value : null;
    }

    private static string? AppIdLine(string? content) =>
        content?.Split('\n').Select(line => line.Split('=', 2)).Where(p => p.Length == 2 && p[0].Trim().Equals("AppId", StringComparison.OrdinalIgnoreCase))
            .Select(p => Number(p[1])).FirstOrDefault(n => n is not null);

    private static string? EpicAppName(string? content)
    {
        if (content is null) return null;
        try
        {
            using var document = JsonDocument.Parse(content, new JsonDocumentOptions { MaxDepth = 16 });
            return document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty("AppName", out var name) && name.ValueKind == JsonValueKind.String
                ? FolderName(name.GetString()) : null;
        }
        catch (JsonException) { return null; }
    }

    /// <summary>A value usable as one folder name, or nothing.</summary>
    private static string? FolderName(string? value)
    {
        value = value?.Trim().TrimEnd('.', ' ');
        return value is { Length: >= 2 and <= 80 } && value.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 ? value : null;
    }
}
