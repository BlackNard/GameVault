using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using GameVault.Core;

namespace GameVault.Infrastructure.Updates;

/// <summary>
/// Remembers the verified installer that is waiting to be installed (<c>pending-update.json</c> in the updates folder) and re-checks it right before it is used,
/// so a file swapped on disk after the download is never started. Never throws: a missing or odd file means "nothing pending".
/// </summary>
public sealed class PendingUpdateStore(string folder)
{
    public const string FileName = "pending-update.json";
    private static readonly Regex InstallerFile = new(@"^GameVault-Setup-[0-9.]{5,30}-win-x64\.exe$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private string StatePath => Path.Combine(folder, FileName);
    public string InstallerPath(PendingUpdate pending) => Path.Combine(folder, pending.InstallerName);

    public PendingUpdate? Read()
    {
        try
        {
            var bytes = File.ReadAllBytes(StatePath);
            if (bytes.Length > 4096) return null;
            using var document = JsonDocument.Parse(bytes);
            var json = document.RootElement;
            if (json.ValueKind != JsonValueKind.Object) return null;
            var tag = Text(json, "tag");
            var version = Text(json, "version");
            var name = Text(json, "installer");
            var sha = Text(json, "sha256");
            if (tag is null || version is null || name is null || sha is null || !InstallerFile.IsMatch(name) || sha.Length != 64 || !sha.All(Uri.IsHexDigit) || !ReleaseVersion.TryParse(version, out _)) return null;
            if (!json.TryGetProperty("size", out var size) || !size.TryGetInt64(out var bytesSize) || bytesSize <= 0) return null;
            DateTimeOffset.TryParse(Text(json, "downloadedAt"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var downloaded);
            var attempts = json.TryGetProperty("attempts", out var a) && a.TryGetInt32(out var count) && count is >= 0 and <= 100 ? count : 0;
            DateTimeOffset? last = DateTimeOffset.TryParse(Text(json, "lastAttemptAt"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var when) ? when : null;
            var archive = Text(json, "safetyArchive");
            var print = Text(json, "fingerprintFile");
            if (archive is not null && !SafeFileName(archive)) archive = null;
            if (print is not null && !SafeFileName(print)) print = null;
            return new PendingUpdate(tag, version, name, sha.ToLowerInvariant(), bytesSize, downloaded, attempts, last, archive, print);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }

    public void Write(PendingUpdate pending)
    {
        try
        {
            Directory.CreateDirectory(folder);
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
            {
                writer.WriteStartObject();
                writer.WriteString("tag", pending.Tag);
                writer.WriteString("version", pending.Version);
                writer.WriteString("installer", pending.InstallerName);
                writer.WriteString("sha256", pending.Sha256);
                writer.WriteNumber("size", pending.Size);
                writer.WriteString("downloadedAt", pending.DownloadedAt.ToString("o", CultureInfo.InvariantCulture));
                writer.WriteNumber("attempts", pending.Attempts);
                if (pending.LastAttemptAt is { } last) writer.WriteString("lastAttemptAt", last.ToString("o", CultureInfo.InvariantCulture));
                if (pending.SafetyArchive is { } archive) writer.WriteString("safetyArchive", archive);
                if (pending.FingerprintFile is { } print) writer.WriteString("fingerprintFile", print);
                writer.WriteEndObject();
            }
            var temporary = StatePath + ".tmp";
            File.WriteAllBytes(temporary, stream.ToArray());
            File.Move(temporary, StatePath, overwrite: true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>Counts one more installer start and returns the updated record.</summary>
    public PendingUpdate RecordAttempt(PendingUpdate pending, DateTimeOffset now)
    {
        var updated = pending with { Attempts = pending.Attempts + 1, LastAttemptAt = now };
        Write(updated);
        return updated;
    }

    /// <summary>Forgets the pending update and deletes its installer file.</summary>
    public void Clear()
    {
        try
        {
            var pending = Read();
            if (pending is not null && File.Exists(InstallerPath(pending))) File.Delete(InstallerPath(pending));
            if (File.Exists(StatePath)) File.Delete(StatePath);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>True when the installer file still exists with exactly the recorded size and SHA-256 (checked again right before it is started).</summary>
    public bool IsIntact(PendingUpdate pending)
    {
        try
        {
            var path = InstallerPath(pending);
            if (!InstallerFile.IsMatch(pending.InstallerName) || !File.Exists(path) || new FileInfo(path).Length != pending.Size) return false;
            return string.Equals(HashFile(path), pending.Sha256, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return false; }
    }

    public static string HashFile(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.SequentialScan);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    /// <summary>A plain file name inside the Safety folder: no path, no drive, no traversal.</summary>
    private static bool SafeFileName(string name) => name.Length is > 0 and <= 120 && name == Path.GetFileName(name) && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && !name.Contains("..", StringComparison.Ordinal);

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 and <= 200 } text ? text : null;
}
