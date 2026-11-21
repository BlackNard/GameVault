using System.Text;

namespace GameVault.Infrastructure.Saves;

public sealed record ManifestFile(string Path, string[] Tags, string[] Os, string[] Stores)
{
    /// <summary>The entry applies on Windows (no OS condition, or one that names Windows).</summary>
    public bool AppliesToWindows => Os.Length == 0 || Os.Contains("windows", StringComparer.OrdinalIgnoreCase);
    public bool IsSave => Tags.Contains("save", StringComparer.OrdinalIgnoreCase);
}

public sealed record ManifestGame(string Name, string? SteamId, string[] InstallDirs, ManifestFile[] Files);

/// <summary>
/// Reads the part of the Ludusavi manifest (YAML) that matters for finding save folders: game name, Steam id, install folder names and the
/// file entries with their tags and OS/store conditions. This is a deliberately small reader for that file's regular shape, not a general YAML parser.
/// Registry entries, cloud info and everything else are ignored. See docs/save-location-database.md.
/// </summary>
public static class LudusaviManifest
{
    public const int MaxLineLength = 4096;

    /// <summary>Parses the manifest. Only games with at least one Windows save entry are kept. Malformed lines are skipped.</summary>
    public static IReadOnlyList<ManifestGame> Parse(TextReader reader)
    {
        var games = new List<ManifestGame>();
        string? name = null;
        string? steamId = null;
        var installDirs = new List<string>();
        var files = new List<ManifestFile>();
        string section = "";
        string? path = null;
        var tags = new List<string>();
        var os = new List<string>();
        var stores = new List<string>();
        string subSection = "";
        var conditionOs = (string?)null;

        void FlushFile()
        {
            if (path is not null) files.Add(new(path, [.. tags], [.. os], [.. stores]));
            path = null; tags.Clear(); os.Clear(); stores.Clear(); subSection = ""; conditionOs = null;
        }
        void FlushGame()
        {
            FlushFile();
            if (name is not null && files.Any(f => f.IsSave && f.AppliesToWindows)) games.Add(new(name, steamId, [.. installDirs], [.. files.Where(f => f.IsSave && f.AppliesToWindows)]));
            name = null; steamId = null; installDirs.Clear(); files.Clear(); section = "";
        }

        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (line.Length == 0 || line.Length > MaxLineLength) continue;
            var indent = 0;
            while (indent < line.Length && line[indent] == ' ') indent++;
            if (indent == line.Length || line[indent] == '#') continue;
            var text = line[indent..].TrimEnd();
            if (indent == 0)
            {
                FlushGame();
                name = text.StartsWith('-') ? null : Key(text).Key;
                continue;
            }
            if (name is null) continue;
            if (indent == 2)
            {
                FlushFile();
                section = Key(text).Key;
                continue;
            }
            switch (section)
            {
                case "files":
                    if (indent == 4) { FlushFile(); path = Key(text).Key; }
                    else if (indent == 6 && path is not null) { subSection = Key(text).Key; conditionOs = null; }
                    else if (indent >= 8 && path is not null)
                    {
                        if (subSection == "tags" && text.StartsWith("- ")) tags.Add(Unquote(text[2..].Trim()));
                        else if (subSection == "when")
                        {
                            var body = text.StartsWith("- ") ? text[2..].Trim() : text;
                            var (key, value) = Key(body);
                            if (key == "os" && value.Length > 0) { os.Add(Unquote(value)); conditionOs = value; }
                            else if (key == "store" && value.Length > 0 && (conditionOs is null || conditionOs.Equals("windows", StringComparison.OrdinalIgnoreCase))) stores.Add(Unquote(value));
                        }
                    }
                    break;
                case "installDir":
                    if (indent == 4) installDirs.Add(Key(text).Key);
                    break;
                case "steam":
                    if (indent == 4)
                    {
                        var (key, value) = Key(text);
                        if (key == "id" && value.Length is > 0 and <= 10 && value.All(char.IsAsciiDigit)) steamId = value;
                    }
                    break;
            }
        }
        FlushGame();
        return games;
    }

    /// <summary>Splits "key: value" (or "key:") where the key may be quoted with ' or ".</summary>
    internal static (string Key, string Value) Key(string text)
    {
        if (text.Length == 0) return ("", "");
        if (text[0] is '\'' or '"')
        {
            var quote = text[0];
            var key = new StringBuilder();
            var i = 1;
            for (; i < text.Length; i++)
            {
                var c = text[i];
                if (quote == '\'' && c == '\'' && i + 1 < text.Length && text[i + 1] == '\'') { key.Append('\''); i++; continue; }
                if (c == quote) break;
                if (quote == '"' && c == '\\' && i + 1 < text.Length) { key.Append(text[++i] switch { 'n' => '\n', 't' => '\t', var other => other }); continue; }
                key.Append(c);
            }
            var rest = i + 1 < text.Length ? text[(i + 1)..].TrimStart() : "";
            return (key.ToString(), rest.StartsWith(':') ? rest[1..].Trim() : "");
        }
        var colon = text.IndexOf(": ", StringComparison.Ordinal);
        if (colon < 0) colon = text.EndsWith(':') ? text.Length - 1 : -1;
        return colon < 0 ? (text, "") : (text[..colon].Trim(), text[(colon + 1)..].Trim());
    }

    private static string Unquote(string value) => value.Length >= 2 && value[0] is '\'' or '"' && value[^1] == value[0] ? value[1..^1] : value;
}
