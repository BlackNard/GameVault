using System.Security.Cryptography;
using System.Text;
using GameVault.Core;
using GameVault.Infrastructure.Storage;

namespace GameVault.Infrastructure.Updates;

/// <summary>What changed between two fingerprints of the library (first paths only; never file contents).</summary>
public sealed record FingerprintDifference(IReadOnlyList<string> Missing, IReadOnlyList<string> Added, IReadOnlyList<string> Changed)
{
    public bool IsEmpty => Missing.Count == 0 && Added.Count == 0 && Changed.Count == 0;
    public int Total => Missing.Count + Added.Count + Changed.Count;

    /// <summary>A short human description with up to <paramref name="limit"/> paths per kind.</summary>
    public string Summary(int limit = 3)
    {
        var parts = new List<string>();
        void Add(string label, IReadOnlyList<string> paths)
        {
            if (paths.Count == 0) return;
            parts.Add($"{paths.Count} {label}: " + string.Join(", ", paths.Take(limit)) + (paths.Count > limit ? ", …" : ""));
        }
        Add("missing", Missing);
        Add("changed", Changed);
        Add("new", Added);
        return string.Join("; ", parts);
    }
}

/// <summary>
/// A fingerprint of everything in <c>Data</c> and <c>Profiles</c>: every folder, and every file with its size and, for normal files, its SHA-256 (the large
/// <c>Backups</c> and <c>Media</c> folders are fingerprinted by name and size only, so taking it stays quick). Compared right before an update's installer starts and
/// right after the new version starts (before it opens the library), it shows whether the update changed anything in the player's data.
/// </summary>
public sealed class LibraryFingerprint
{
    private readonly string[] lines;

    private LibraryFingerprint(string[] sortedLines) => lines = sortedLines;

    public IReadOnlyList<string> Lines => lines;
    public int FileCount => lines.Count(l => l.StartsWith("F|", StringComparison.Ordinal));

    /// <summary>One SHA-256 over all lines.</summary>
    public string Digest => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", lines)))).ToLowerInvariant();

    public static LibraryFingerprint Compute(ApplicationPaths paths, CancellationToken cancellationToken = default)
    {
        var result = new List<string>();
        foreach (var top in new[] { paths.Data, paths.Profiles })
        {
            if (!Directory.Exists(top)) continue;
            var pending = new Stack<string>();
            pending.Push(top);
            while (pending.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var folder = pending.Pop();
                result.Add("D|" + Relative(paths, folder));
                foreach (var entry in new DirectoryInfo(folder).EnumerateFileSystemInfos())
                {
                    var relative = Relative(paths, entry.FullName);
                    if ((entry.Attributes & FileAttributes.ReparsePoint) != 0) { result.Add("L|" + relative); continue; }
                    if (entry is DirectoryInfo) { pending.Push(entry.FullName); continue; }
                    var size = ((FileInfo)entry).Length;
                    result.Add($"F|{relative}|{size}|{(DataArchive.IsBulky(relative) ? "-" : HashFile(entry.FullName))}");
                }
            }
        }
        result.Sort(StringComparer.Ordinal);
        return new LibraryFingerprint([.. result]);
    }

    public string ToText() => string.Join("\n", lines);

    public static LibraryFingerprint? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var parsed = text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.TrimEnd('\r')).ToArray();
        if (parsed.Any(l => l.Length < 3 || l[1] != '|' || l[0] is not ('D' or 'F' or 'L'))) return null;
        Array.Sort(parsed, StringComparer.Ordinal);
        return new LibraryFingerprint(parsed);
    }

    /// <summary>What is different in <paramref name="after"/> compared with this (the fingerprint taken before).</summary>
    public FingerprintDifference Compare(LibraryFingerprint after)
    {
        var before = Index(lines);
        var now = Index(after.lines);
        var missing = before.Keys.Where(k => !now.ContainsKey(k)).OrderBy(k => k, StringComparer.Ordinal).ToArray();
        var added = now.Keys.Where(k => !before.ContainsKey(k)).OrderBy(k => k, StringComparer.Ordinal).ToArray();
        var changed = before.Where(p => now.TryGetValue(p.Key, out var value) && value != p.Value).Select(p => p.Key).OrderBy(k => k, StringComparer.Ordinal).ToArray();
        return new FingerprintDifference(missing, added, changed);
    }

    private static Dictionary<string, string> Index(IEnumerable<string> source)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in source)
        {
            var rest = line[2..];
            var split = line[0] == 'F' ? rest.IndexOf('|') : -1;
            result[split > 0 ? rest[..split] : rest] = line;    // keyed by path: a file turning into a folder shows as changed
        }
        return result;
    }

    private static string Relative(ApplicationPaths paths, string full) => Path.GetRelativePath(paths.Root, full).Replace('\\', '/');

    private static string HashFile(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 81920, FileOptions.SequentialScan);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}
