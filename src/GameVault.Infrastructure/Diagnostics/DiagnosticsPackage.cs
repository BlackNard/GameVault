using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using GameVault.Core;

namespace GameVault.Infrastructure.Diagnostics;

/// <summary>What the caller knows about this PC and the library. Everything here is redacted before it is written.</summary>
public sealed record DiagnosticsInput(string AppVersion, string OsVersion, string Runtime, string Gpu, bool NvidiaAppDetected, bool CapturePrerequisitesPresent,
    string SettingsJson, int GameCount, int GamesWithPlaytime, string[] GameNames, string[] LogLines, bool IncludeGameNames, string? ClipStatistics = null);

public sealed record DiagnosticsFile(string Name, string Text);

/// <summary>
/// Builds the diagnostics zip for bug reports (ticket #86): environment, redacted settings, counts, recent log lines.
/// Never includes save contents, notes, covers, tokens or full paths under the user profile; game names only on request.
/// </summary>
public static class DiagnosticsPackage
{
    private static readonly Regex LongToken = new(@"\b[A-Za-z0-9_\-+/=]{32,}\b", RegexOptions.Compiled);

    public static IReadOnlyList<DiagnosticsFile> Build(ApplicationPaths paths, DiagnosticsInput input)
    {
        var redact = Redactor(paths, input);
        var files = new List<DiagnosticsFile>
        {
            new("environment.txt", redact(string.Join(Environment.NewLine,
                $"GameVault version: {input.AppVersion}", $"Windows: {input.OsVersion}", $".NET runtime: {input.Runtime}", $"Graphics: {input.Gpu}",
                $"NVIDIA App detected: {(input.NvidiaAppDetected ? "yes" : "no")}", $"Capture prerequisites present: {(input.CapturePrerequisitesPresent ? "yes" : "no")}",
                $"Created: {DateTimeOffset.Now:u}"))),
            new("settings.json", redact(input.SettingsJson)),
            new("counts.txt", Count(paths, input)),
            new("log.txt", redact(string.Join(Environment.NewLine, input.LogLines.TakeLast(400)))),
        };
        if (input.IncludeGameNames) files.Add(new("games.txt", string.Join(Environment.NewLine, input.GameNames)));
        if (!string.IsNullOrWhiteSpace(input.ClipStatistics)) files.Add(new("capture-statistics.txt", redact(input.ClipStatistics)));
        return files;
    }

    public static string Create(ApplicationPaths paths, DiagnosticsInput input, string destinationFolder)
    {
        if (string.IsNullOrWhiteSpace(destinationFolder) || !Path.IsPathFullyQualified(destinationFolder) || !Directory.Exists(destinationFolder))
            throw new IOException("Choose an existing folder for the diagnostics package.");
        var files = Build(paths, input);
        var target = Path.Combine(destinationFolder, $"GameVault-diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.zip");
        var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
                foreach (var file in files)
                {
                    using var writer = new StreamWriter(zip.CreateEntry(file.Name).Open(), new UTF8Encoding(false));
                    writer.Write(file.Text);
                }
            File.Move(temporary, target, overwrite: false);
            return target;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    /// <summary>Counts only. Read straight from the folders so a damaged or read-only library still produces a package.</summary>
    private static string Count(ApplicationPaths paths, DiagnosticsInput input)
    {
        int profiles = 0, backups = 0, media = 0, notes = 0;
        try
        {
            if (Directory.Exists(paths.Profiles))
                foreach (var folder in Directory.EnumerateDirectories(paths.Profiles))
                {
                    profiles++;
                    backups += SafeCount(() => Directory.EnumerateDirectories(Path.Combine(folder, "Backups")).Count());
                    media += SafeCount(() => Directory.EnumerateFiles(Path.Combine(folder, "Media"), "*", SearchOption.AllDirectories).Count());
                    notes += SafeCount(() => Directory.EnumerateFiles(Path.Combine(folder, "Notes"), "*", SearchOption.AllDirectories).Count());
                }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        return string.Join(Environment.NewLine, $"Games loaded: {input.GameCount}", $"Profile folders: {profiles}", $"Games with recorded playtime: {input.GamesWithPlaytime}", $"Backups: {backups}", $"Media files: {media}", $"Note files: {notes}");
    }

    private static int SafeCount(Func<int> count)
    {
        try { return count(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return 0; }
    }

    /// <summary>Removes the user name, user-profile and application folders, game names (unless included) and anything that looks like a token.</summary>
    public static Func<string, string> Redactor(ApplicationPaths paths, DiagnosticsInput input)
    {
        var replacements = new List<(string From, string To)>
        {
            (paths.Root, "<app folder>"),
            (Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "<user profile>"),
        };
        var user = Environment.UserName;
        if (user.Length >= 3) replacements.Add((user, "<user>"));
        if (!input.IncludeGameNames)
        {
            var number = 0;
            foreach (var name in input.GameNames.Where(n => n.Length >= 2).Distinct().OrderByDescending(n => n.Length)) replacements.Add((name, $"<game {++number}>"));
        }
        replacements = [.. replacements.Where(r => !string.IsNullOrWhiteSpace(r.From)).OrderByDescending(r => r.From.Length)];
        var variants = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (from, to) in replacements)
            foreach (var form in new[] { from, from.Replace('\\', '/'), from.Replace("\\", "\\\\") })
                variants.TryAdd(form, to);
        var pattern = new Regex(string.Join("|", variants.Keys.OrderByDescending(k => k.Length).Select(Regex.Escape)), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        // One pass, so a replacement is never searched again (a user called "profile" cannot damage "<user profile>").
        return text => LongToken.Replace(variants.Count == 0 ? text : pattern.Replace(text, m => variants[m.Value]), "<redacted>");
    }
}
