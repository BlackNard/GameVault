using System.Text.Json;
using System.Text.Json.Serialization;
using GameVault.Core;

namespace GameVault.Infrastructure.Storage;

internal sealed class JsonDocuments(ManagedPaths paths, Action<string>? beforeCommit = null)
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        RespectRequiredConstructorParameters = true,
        RespectNullableAnnotations = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };

    public StoredDocument<T> Read<T>(string file, Func<T, bool> valid) where T : class
    {
        var primary = ReadSingle(file, valid);
        if (primary.State is not (DocumentState.Corrupt or DocumentState.Missing)) return primary;
        var previous = ReadSingle(file + ".bak", valid);
        if (previous.State == DocumentState.Ready)
            return new(previous.Value, DocumentState.Recovered,
                $"{Path.GetFileName(file)} needs recovery. A previous valid copy is available read-only; original files were preserved.");
        if (primary.State == DocumentState.Missing && previous.State != DocumentState.Missing)
            return new(null, previous.State, $"{Path.GetFileName(file)} is missing and its previous copy needs attention. Existing files were preserved.");
        return primary;
    }

    private StoredDocument<T> ReadSingle<T>(string file, Func<T, bool> valid) where T : class
    {
        try
        {
            paths.Check(file);
            if (Directory.Exists(file)) throw new IOException("A document path points to a folder.");
            if (!File.Exists(file)) return new(null, DocumentState.Missing);
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var json = JsonDocument.Parse(stream);
            if (json.RootElement.ValueKind != JsonValueKind.Object ||
                !json.RootElement.TryGetProperty("schemaVersion", out var version) ||
                version.ValueKind != JsonValueKind.Number ||
                !version.TryGetInt32(out var number)) throw new JsonException();
            if (number != 1) return new(null, DocumentState.Unsupported,
                $"{Path.GetFileName(file)} uses an unsupported data version ({number}). Use a compatible GameVault version; this file will not be changed.");
            var value = json.Deserialize<T>(Options);
            if (value is null || !valid(value)) throw new JsonException();
            return new(value, DocumentState.Ready);
        }
        catch (JsonException)
        {
            return new(null, DocumentState.Corrupt, $"{Path.GetFileName(file)} contains invalid or incomplete data. It has not been reset.");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return new(null, DocumentState.Unavailable, $"{Path.GetFileName(file)} cannot be read safely. Check access and linked paths; existing data has been kept.");
        }
    }

    public void Write<T>(string file, T value, Func<T, bool> valid) where T : class
    {
        if (!valid(value)) throw new ArgumentException("The document contains invalid settings, identity, paths, or values.", nameof(value));
        var current = Read(file, valid);
        RequireWritable(current);
        paths.EnsureDirectory(Path.GetDirectoryName(file)!);
        paths.Check(file + ".bak");
        var temporary = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                JsonSerializer.Serialize(stream, value, Options);
                stream.Flush(flushToDisk: true);
            }
            beforeCommit?.Invoke(file);
            RequireWritable(Read(file, valid));
            paths.Check(file);
            paths.Check(file + ".bak");
            if (current.State == DocumentState.Missing) File.Move(temporary, file, overwrite: false);
            else File.Replace(temporary, file, file + ".bak");
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    public static void RequireWritable<T>(StoredDocument<T> document)
    {
        if (document.IsReadOnly) throw new IOException(document.Message ?? "This document is read-only and needs recovery.");
    }
}
