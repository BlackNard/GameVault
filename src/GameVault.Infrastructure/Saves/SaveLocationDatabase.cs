using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using GameVault.Core;
using GameVault.Infrastructure.Launching;

namespace GameVault.Infrastructure.Saves;

/// <summary>Extra save-folder candidates from a community database. Candidates are only suggestions: the usual confirmation flow still decides.</summary>
public interface ISaveLocationSource
{
    IReadOnlyList<string> Candidates(GameProfile profile, SaveDiscoveryEnvironment environment);
}

public sealed record SaveDatabaseStatus(bool Available, int Games, DateTimeOffset? DownloadedAt);

/// <summary>
/// The Ludusavi manifest (MIT) as a source of candidate save folders (ticket #84). The manifest is compiled from PCGamingWiki, whose text is
/// CC BY-NC-SA, so GameVault does not bundle it: the owner downloads it on request into Cache/ and it works offline from that copy.
/// </summary>
public sealed class SaveLocationDatabase : ISaveLocationSource
{
    public const string ManifestUrl = "https://raw.githubusercontent.com/mtkennerly/ludusavi-manifest/data/data/manifest.yaml";
    public const string Attribution = "Save locations come from the Ludusavi manifest (MIT), compiled from PCGamingWiki (https://www.pcgamingwiki.com, Creative Commons Attribution-NonCommercial-ShareAlike 3.0).";
    private const long MaxDownloadBytes = 96L * 1024 * 1024;
    private const int MaxCandidates = 24, MaxExpansion = 64;

    private readonly string manifestFile;
    private readonly string metaFile;
    private readonly object gate = new();
    private IReadOnlyList<ManifestGame>? games;
    private Dictionary<string, List<ManifestGame>> bySteam = [];
    private Dictionary<string, List<ManifestGame>> byName = [];

    public SaveLocationDatabase(ApplicationPaths paths)
    {
        var cache = Path.Combine(paths.Root, "Cache");
        manifestFile = Path.Combine(cache, "ludusavi-manifest.yaml");
        metaFile = Path.Combine(cache, "ludusavi-manifest.json");
    }

    private sealed record Meta(DateTimeOffset DownloadedAt, string? ETag, string Sha256, int Games);

    public SaveDatabaseStatus Status()
    {
        EnsureLoaded();
        lock (gate) return new(games is { Count: > 0 }, games?.Count ?? 0, ReadMeta()?.DownloadedAt);
    }

    private Meta? ReadMeta()
    {
        try { return File.Exists(metaFile) ? JsonSerializer.Deserialize<Meta>(File.ReadAllText(metaFile), new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) : null; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }

    private void EnsureLoaded()
    {
        lock (gate)
        {
            if (games is not null) return;
            try
            {
                if (File.Exists(manifestFile))
                {
                    using var reader = new StreamReader(manifestFile);
                    Index(LudusaviManifest.Parse(reader));
                    return;
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
            Index([]);
        }
    }

    internal void Index(IReadOnlyList<ManifestGame> parsed)
    {
        lock (gate)
        {
            games = parsed;
            bySteam = [];
            byName = [];
            foreach (var game in parsed)
            {
                if (game.SteamId is not null) Add(bySteam, game.SteamId, game);
                foreach (var key in new[] { game.Name }.Concat(game.InstallDirs).Select(Normalize).Where(k => k.Length >= 3).Distinct()) Add(byName, key, game);
            }
        }
        static void Add(Dictionary<string, List<ManifestGame>> map, string key, ManifestGame game)
        {
            if (!map.TryGetValue(key, out var list)) map[key] = list = [];
            list.Add(game);
        }
    }

    private static string Normalize(string value) => new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    // ---------- download ----------
    /// <summary>Downloads the manifest once, on request. Nothing about the user or their games is sent. Keeps the previous copy if the new one is unusable.</summary>
    public async Task<SaveDatabaseStatus> DownloadAsync(HttpMessageHandler? handler, CancellationToken cancellationToken)
    {
        using var client = new HttpClient(handler ?? new SocketsHttpHandler { AllowAutoRedirect = true, MaxAutomaticRedirections = 3, UseCookies = false }) { Timeout = TimeSpan.FromMinutes(3) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("GameVault", "1"));
        var previous = ReadMeta();
        using var request = new HttpRequestMessage(HttpMethod.Get, ManifestUrl);
        if (previous?.ETag is { Length: > 0 } etag && File.Exists(manifestFile) && EntityTagHeaderValue.TryParse(etag, out var tag)) request.Headers.IfNoneMatch.Add(tag);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.NotModified) return Status();
        if (!response.IsSuccessStatusCode) throw new IOException($"The save-location database could not be downloaded (status {(int)response.StatusCode}).");
        if (response.Content.Headers.ContentLength > MaxDownloadBytes) throw new IOException("The save-location database is larger than expected, so it was not downloaded.");
        Directory.CreateDirectory(Path.GetDirectoryName(manifestFile)!);
        var temporary = manifestFile + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            await using (var target = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                var buffer = new byte[81920];
                long total = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    total += read;
                    if (total > MaxDownloadBytes) throw new IOException("The save-location database is larger than expected, so it was not downloaded.");
                    hash.AppendData(buffer, 0, read);
                    await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                }
            }
            // Only a file that parses into a plausible number of games replaces the current copy.
            IReadOnlyList<ManifestGame> parsed;
            using (var reader = new StreamReader(temporary)) parsed = LudusaviManifest.Parse(reader);
            if (parsed.Count < 500) throw new IOException("The downloaded file does not look like the save-location database, so it was not used.");
            File.Move(temporary, manifestFile, overwrite: true);
            File.WriteAllText(metaFile, JsonSerializer.Serialize(new Meta(DateTimeOffset.UtcNow, response.Headers.ETag?.ToString(), Convert.ToHexString(hash.GetHashAndReset()), parsed.Count)));
            Index(parsed);
            return Status();
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    // ---------- lookup ----------
    public IReadOnlyList<string> Candidates(GameProfile profile, SaveDiscoveryEnvironment environment)
    {
        EnsureLoaded();
        List<ManifestGame> matches;
        string? steamId = null;
        string? installRoot = null;
        try
        {
            var plan = new StoreGameLauncher().Resolve(profile.ExecutablePath, null);
            if (plan is not null) { installRoot = plan.InstallRoot; if (plan.StoreKey.StartsWith("Steam:", StringComparison.Ordinal)) steamId = plan.StoreKey[6..]; }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException) { }
        var folder = Path.GetDirectoryName(profile.ExecutablePath);
        installRoot ??= folder;
        lock (gate)
        {
            matches = [];
            if (steamId is not null && bySteam.TryGetValue(steamId, out var byId)) matches.AddRange(byId);
            // Exact name matches only: a near match could point at another game's saves.
            if (matches.Count == 0)
                foreach (var key in new[] { profile.Name, folder is null ? "" : Path.GetFileName(folder), installRoot is null ? "" : Path.GetFileName(installRoot) }.Select(Normalize).Where(k => k.Length >= 3).Distinct())
                    if (byName.TryGetValue(key, out var byKey)) matches.AddRange(byKey);
        }
        var result = new List<string>();
        foreach (var game in matches.Distinct())
            foreach (var file in game.Files)
            {
                if (result.Count >= MaxCandidates) return result;
                foreach (var expanded in Expand(file.Path, game, steamId, installRoot, environment))
                    // Only places that exist now: a database lists many possible folders and most do not apply to this PC.
                    if (result.Count < MaxCandidates && (Directory.Exists(expanded) || File.Exists(expanded)) && !result.Contains(expanded, StringComparer.OrdinalIgnoreCase)) result.Add(expanded);
            }
        return result;
    }

    /// <summary>Turns a manifest path into real Windows paths: placeholders become known folders (several Steam accounts give several paths) and * or ? match existing folders. Anything unknown or unsafe yields nothing.</summary>
    public static IReadOnlyList<string> Expand(string template, ManifestGame game, string? steamId, string? installRoot, SaveDiscoveryEnvironment env)
    {
        if (string.IsNullOrWhiteSpace(template) || template.Length > 600) return [];
        var baseFolder = installRoot ?? "";
        var userIds = SteamUserIds(env);
        var variants = new List<string> { template.Replace('\\', '/') };
        bool Replace(string token, IEnumerable<string> values)
        {
            if (!variants.Any(v => v.Contains(token, StringComparison.Ordinal))) return true;
            var list = values.Where(v => !string.IsNullOrEmpty(v)).ToArray();
            if (list.Length == 0) return false;
            variants = [.. variants.SelectMany(v => v.Contains(token, StringComparison.Ordinal) ? list.Select(x => v.Replace(token, x.Replace('\\', '/'), StringComparison.Ordinal)) : [v]).Take(MaxExpansion)];
            return true;
        }
        var publicRoot = Path.GetDirectoryName(env.PublicDocuments) ?? "";
        var ok = Replace("<home>", [env.Home]) && Replace("<winAppData>", [env.Roaming]) && Replace("<winLocalAppData>", [env.Local]) &&
            Replace("<winLocalAppDataLow>", [Path.Combine(env.Home, "AppData", "LocalLow")]) && Replace("<winDocuments>", [env.Documents]) &&
            Replace("<winPublic>", [publicRoot]) && Replace("<winProgramData>", [Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData)]) &&
            Replace("<osUserName>", [Environment.UserName]) && Replace("<storeGameId>", steamId is null ? [] : [steamId]) && Replace("<storeUserId>", userIds) &&
            Replace("<base>", [baseFolder]) && Replace("<root>", [Path.GetDirectoryName(baseFolder) ?? ""]) && Replace("<game>", [Path.GetFileName(baseFolder)]);
        if (!ok) return [];
        var result = new List<string>();
        foreach (var variant in variants)
        {
            if (variant.Contains('<') || variant.Contains('>')) continue;
            var path = variant.Replace('/', Path.DirectorySeparatorChar);
            if (!Path.IsPathFullyQualified(path) || path.Split(Path.DirectorySeparatorChar).Any(part => part == ".." || part == ".")) continue;
            if (path.IndexOfAny(['*', '?']) >= 0) result.AddRange(Glob(path));
            else if (path.IndexOfAny(Path.GetInvalidPathChars()) < 0) result.Add(Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar));
        }
        return [.. result.Distinct(StringComparer.OrdinalIgnoreCase).Take(MaxExpansion)];
    }

    private static string[] SteamUserIds(SaveDiscoveryEnvironment env)
    {
        var ids = new List<string>();
        foreach (var steam in env.SteamRoots)
        {
            try
            {
                var users = Path.Combine(steam, "userdata");
                if (Directory.Exists(users)) ids.AddRange(Directory.EnumerateDirectories(users).Select(Path.GetFileName).Where(n => n is { Length: > 0 and <= 12 } && n.All(char.IsAsciiDigit) && n != "0").Take(16)!);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
        return [.. ids.Distinct()];
    }

    /// <summary>Expands * and ? one folder level at a time over existing folders only, without following links.</summary>
    private static IEnumerable<string> Glob(string pattern)
    {
        var parts = pattern.Split(Path.DirectorySeparatorChar);
        var current = new List<string> { parts[0] + Path.DirectorySeparatorChar };
        for (var i = 1; i < parts.Length && current.Count > 0; i++)
        {
            var next = new List<string>();
            foreach (var folder in current)
            {
                if (parts[i].IndexOfAny(['*', '?']) < 0) { next.Add(Path.Combine(folder, parts[i])); continue; }
                try
                {
                    foreach (var entry in new DirectoryInfo(folder).EnumerateFileSystemInfos(parts[i]).Take(MaxExpansion))
                        if ((entry.Attributes & FileAttributes.ReparsePoint) == 0) next.Add(entry.FullName);
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException) { }
                if (next.Count > MaxExpansion) break;
            }
            current = [.. next.Take(MaxExpansion)];
        }
        return current.Select(p => p.TrimEnd(Path.DirectorySeparatorChar));
    }
}
