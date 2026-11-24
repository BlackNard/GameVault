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
            best = new UpdateInfo(version.ToString(), tag, url, Flag(release, "prerelease") || version.IsPreRelease, published);
            bestVersion = version;
        }
        return best;
    }

    /// <summary>True only for https://github.com/&lt;repository&gt;/releases/... so a hostile or odd answer can never make the app open another site.</summary>
    public bool IsOurReleaseLink(string url)
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
