using System.Globalization;
using System.Text;
using System.Text.Json;
using GameVault.Core;
using GameVault.Infrastructure.Metadata;

namespace GameVault.Infrastructure.Updates;

/// <summary>
/// Asks GitHub's public releases list whether a newer GameVault than the running one has been published. One anonymous GET to a fixed HTTPS address
/// through <see cref="PoliteHttp"/> (timeout, size cap, no redirects, no cookies); nothing about the player or the PC is sent. It only reports; it never
/// downloads or installs anything. Release links are accepted only when they point at this repository's releases on github.com.
/// </summary>
public sealed class UpdateChecker(PoliteHttp http, string repository = UpdateChecker.DefaultRepository)
{
    public const string DefaultRepository = "BlackNard/GameVault";
    public const int MaxBytes = 512 * 1024;

    public string ReleasesUrl => $"https://api.github.com/repos/{repository}/releases?per_page=20";

    public async Task<UpdateCheckResult> CheckAsync(string currentVersion, CancellationToken cancellationToken)
    {
        if (!ReleaseVersion.TryParse(currentVersion, out var current)) return new(UpdateStatus.Failed, currentVersion, Message: "this version number is not recognised");
        try
        {
            var bytes = await http.GetBytesAsync(ReleasesUrl, cancellationToken, MaxBytes, "application/vnd.github+json").ConfigureAwait(false);
            var newest = Newest(bytes, current);
            return newest is null ? new(UpdateStatus.UpToDate, current.ToString()) : new(UpdateStatus.Available, current.ToString(), newest);
        }
        catch (FileNotFoundException) { return new(UpdateStatus.Failed, current.ToString(), Message: "GitHub did not find the releases page"); }
        catch (IOException error) { return new(UpdateStatus.Failed, current.ToString(), Message: error.Message.TrimEnd('.')); }
        catch (JsonException) { return new(UpdateStatus.Failed, current.ToString(), Message: "GitHub sent an answer GameVault could not read"); }
    }

    /// <summary>The newest published (non-draft) release whose version is above <paramref name="current"/>, or null.</summary>
    public UpdateInfo? Newest(byte[] json, ReleaseVersion current)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array) throw new JsonException("not a list");
        UpdateInfo? best = null;
        ReleaseVersion? bestVersion = null;
        foreach (var release in document.RootElement.EnumerateArray())
        {
            if (release.ValueKind != JsonValueKind.Object) continue;
            if (Flag(release, "draft")) continue;
            var tag = Text(release, "tag_name");
            var url = Text(release, "html_url");
            if (tag is null || url is null || !ReleaseVersion.TryParse(tag, out var version) || !IsOurReleaseLink(url)) continue;
            if (version <= current || bestVersion is not null && version <= bestVersion) continue;
            DateTimeOffset? published = DateTimeOffset.TryParse(Text(release, "published_at"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var when) ? when : null;
            best = new UpdateInfo(version.ToString(), tag, url, Flag(release, "prerelease") || version.IsPreRelease, published, FindInstaller(release, tag, version));
            bestVersion = version;
        }
        return best;
    }

    public const long MinInstallerBytes = 1_000_000;
    public const long MaxInstallerBytes = 400_000_000;
    private static readonly System.Text.RegularExpressions.Regex InstallerName = new(@"^GameVault-Setup-(?<v>\d{1,6}\.\d{1,6}\.\d{1,6}(?:\.\d{1,6})?)-win-x64\.exe$", System.Text.RegularExpressions.RegexOptions.CultureInvariant | System.Text.RegularExpressions.RegexOptions.Compiled);
    private static readonly System.Text.RegularExpressions.Regex Sha256Digest = new("^sha256:(?<h>[0-9a-fA-F]{64})$", System.Text.RegularExpressions.RegexOptions.CultureInvariant | System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>
    /// The release's setup file (<c>GameVault-Setup-&lt;version&gt;-win-x64.exe</c>) with its GitHub-computed SHA-256 and the <c>.sha256</c> asset next to it. Null when the release
    /// has no such file, its name does not match the tag, its link is not a download link of this release, or its size is not believable.
    /// </summary>
    private UpdateInstaller? FindInstaller(JsonElement release, string tag, ReleaseVersion version)
    {
        if (!release.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array) return null;
        var byName = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var asset in assets.EnumerateArray().Take(60))
            if (asset.ValueKind == JsonValueKind.Object && Text(asset, "name") is { } name) byName[name] = asset;
        foreach (var (name, asset) in byName)
        {
            var match = InstallerName.Match(name);
            if (!match.Success || !ReleaseVersion.TryParse(match.Groups["v"].Value, out var fileVersion) || fileVersion.Major != version.Major || fileVersion.Minor != version.Minor || fileVersion.Patch != version.Patch || fileVersion.Revision != version.Revision) continue;
            var url = Text(asset, "browser_download_url");
            if (url is null || !IsDownloadLink(url, tag, name, repository)) continue;
            if (!asset.TryGetProperty("size", out var size) || size.ValueKind != JsonValueKind.Number || !size.TryGetInt64(out var bytes) || bytes < MinInstallerBytes || bytes > MaxInstallerBytes) continue;
            string? digest = null;
            if (Text(asset, "digest") is { } text && Sha256Digest.Match(text) is { Success: true } hash) digest = hash.Groups["h"].Value.ToLowerInvariant();
            string? checksumUrl = null;
            if (byName.TryGetValue(name + ".sha256", out var sums) && Text(sums, "browser_download_url") is { } sumsUrl && IsDownloadLink(sumsUrl, tag, name + ".sha256", repository)) checksumUrl = sumsUrl;
            if (digest is null && checksumUrl is null) continue;                      // nothing to verify the file against: not offered
            return new UpdateInstaller(name, url, bytes, digest, checksumUrl);
        }
        return null;
    }

    /// <summary>True only for https://github.com/&lt;repository&gt;/releases/download/&lt;tag&gt;/&lt;name&gt; (exact path, no query, no user info).</summary>
    public static bool IsDownloadLink(string url, string tag, string name, string repository = DefaultRepository)
    {
        if (url.Length > 400 || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || !uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)) return false;
        if (!string.IsNullOrEmpty(uri.UserInfo) || !uri.IsDefaultPort || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment)) return false;
        return string.Equals(uri.AbsolutePath, $"/{repository}/releases/download/{Uri.EscapeDataString(tag)}/{Uri.EscapeDataString(name)}", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>True only for https://github.com/&lt;repository&gt;/releases/... so a hostile or odd answer can never make the app open another site.</summary>
    public bool IsOurReleaseLink(string url) => IsReleaseLink(url, repository);

    public static bool IsReleaseLink(string url, string repository = DefaultRepository)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || !uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)) return false;
        if (!string.IsNullOrEmpty(uri.UserInfo) || !uri.IsDefaultPort || url.Length > 300) return false;
        return uri.AbsolutePath.StartsWith($"/{repository}/releases", StringComparison.OrdinalIgnoreCase) &&
               (uri.AbsolutePath.Length == $"/{repository}/releases".Length || uri.AbsolutePath[$"/{repository}/releases".Length] == '/');
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 and <= 300 } text ? text : null;

    private static bool Flag(JsonElement element, string name) => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
}

/// <summary>When the last update check ran (Cache/update-check.txt, one ISO time stamp). Never throws; a missing or odd file just means "never".</summary>
public static class UpdateCheckStamp
{
    private static string PathFor(string root) => Path.Combine(root, "Cache", "update-check.txt");

    public static DateTimeOffset? Read(string root)
    {
        try
        {
            var text = File.ReadAllText(PathFor(root), Encoding.UTF8).Trim();
            return DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var when) ? when : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return null; }
    }

    public static void Write(string root, DateTimeOffset now)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PathFor(root))!);
            File.WriteAllText(PathFor(root), now.ToString("o", CultureInfo.InvariantCulture), new UTF8Encoding(false));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }
}
