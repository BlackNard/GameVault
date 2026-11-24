using System.Globalization;
using System.Text;
using System.Text.Json;
using GameVault.Core;

namespace GameVault.Infrastructure.Updates;

/// <summary>
/// Remembers the newest release the last check found and which version the player dismissed (Cache/update-notice.json), so the notice in the
/// bell is still there after a restart even when the start-up check is skipped (it runs at most every 12 hours). Never throws; damaged or odd
/// content simply means "nothing known". Whether a cached release is still newer than the running app is decided by the caller.
/// </summary>
public sealed record UpdateNoticeState(UpdateInfo? Latest, string? DismissedTag)
{
    public static UpdateNoticeState Empty { get; } = new(null, null);
}

public static class UpdateNoticeCache
{
    private static string PathFor(string root) => Path.Combine(root, "Cache", "update-notice.json");

    public static UpdateNoticeState Read(string root)
    {
        try
        {
            var bytes = File.ReadAllBytes(PathFor(root));
            if (bytes.Length > 16384) return UpdateNoticeState.Empty;
            using var document = JsonDocument.Parse(bytes);
            var json = document.RootElement;
            if (json.ValueKind != JsonValueKind.Object) return UpdateNoticeState.Empty;
            var dismissed = Text(json, "dismissedTag");
            UpdateInfo? latest = null;
            if (Text(json, "tag") is { } tag && Text(json, "version") is { } version && Text(json, "url") is { } url)
                latest = new UpdateInfo(version, tag, url, json.TryGetProperty("preRelease", out var p) && p.ValueKind == JsonValueKind.True,
                    DateTimeOffset.TryParse(Text(json, "publishedAt"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var when) ? when : null,
                    ReadInstaller(json));
            return new UpdateNoticeState(latest, dismissed);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { return UpdateNoticeState.Empty; }
    }

    private static UpdateInstaller? ReadInstaller(JsonElement json)
    {
        if (!json.TryGetProperty("installer", out var node) || node.ValueKind != JsonValueKind.Object) return null;
        if (Text(node, "name") is not { } name || Text(node, "url") is not { } url) return null;
        if (!node.TryGetProperty("size", out var size) || size.ValueKind != JsonValueKind.Number || !size.TryGetInt64(out var bytes)) return null;
        var sha = Text(node, "sha256");
        if (sha is not null && (sha.Length != 64 || !sha.All(Uri.IsHexDigit))) sha = null;
        return new UpdateInstaller(name, url, bytes, sha, Text(node, "checksumUrl"));
    }

    /// <summary>Stores what the latest check found (null when nothing newer exists) and keeps the dismissed version.</summary>
    public static void WriteLatest(string root, UpdateInfo? latest)
    {
        var current = Read(root);
        Write(root, new UpdateNoticeState(latest, current.DismissedTag));
    }

    public static void Dismiss(string root, string tag)
    {
        var current = Read(root);
        Write(root, new UpdateNoticeState(current.Latest, tag));
    }

    /// <summary>True when a cached release should still be shown: it is newer than the running app and was not dismissed.</summary>
    public static bool ShouldShow(UpdateNoticeState state, ReleaseVersion current) =>
        state.Latest is { } latest && ReleaseVersion.TryParse(latest.Version, out var version) && version > current &&
        !string.Equals(state.DismissedTag, latest.Tag, StringComparison.Ordinal);

    private static void Write(string root, UpdateNoticeState state)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PathFor(root))!);
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
            {
                writer.WriteStartObject();
                if (state.Latest is { } latest)
                {
                    writer.WriteString("tag", latest.Tag);
                    writer.WriteString("version", latest.Version);
                    writer.WriteString("url", latest.Url);
                    writer.WriteBoolean("preRelease", latest.IsPreRelease);
                    if (latest.PublishedAt is { } published) writer.WriteString("publishedAt", published.ToString("o", CultureInfo.InvariantCulture));
                    if (latest.Installer is { } installer)
                    {
                        writer.WriteStartObject("installer");
                        writer.WriteString("name", installer.Name);
                        writer.WriteString("url", installer.Url);
                        writer.WriteNumber("size", installer.Size);
                        if (installer.Sha256 is { } sha) writer.WriteString("sha256", sha);
                        if (installer.ChecksumUrl is { } sums) writer.WriteString("checksumUrl", sums);
                        writer.WriteEndObject();
                    }
                }
                if (state.DismissedTag is { } dismissed) writer.WriteString("dismissedTag", dismissed);
                writer.WriteEndObject();
            }
            File.WriteAllBytes(PathFor(root), stream.ToArray());
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 and <= 300 } text ? text : null;
}
