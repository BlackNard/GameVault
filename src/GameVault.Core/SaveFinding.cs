namespace GameVault.Core;

/// <summary>How GameVault came to look at a folder (issue #111).</summary>
public enum SaveCandidateSource { KnownRule, StoreNumber, Database, PublisherFolder, GameNamed, InstallFolder, NameSearch, Activity }

/// <summary>High: selected and backed up. Medium: the player is asked. Low: only listed.</summary>
public enum SaveConfidence { Low, Medium, High }

public sealed record SaveCandidateFacts(string Path, SaveCandidateSource Source, bool NamedForGame, bool HasSaveFiles, bool OnlySettingsFiles,
    bool WrittenDuringPlay, int SessionsSeen, bool WrittenWhileOtherGameRan);

public static class SaveCandidateScore
{
    private const int High = 6, Medium = 3;

    public static int Points(SaveCandidateFacts f) =>
        f.Source switch
        {
            SaveCandidateSource.KnownRule => 4,
            SaveCandidateSource.StoreNumber or SaveCandidateSource.Database => 3,
            SaveCandidateSource.PublisherFolder or SaveCandidateSource.GameNamed => 2,
            SaveCandidateSource.InstallFolder or SaveCandidateSource.NameSearch => 1,
            _ => 0
        }
        + (f.NamedForGame ? 2 : 0)
        + (f.WrittenDuringPlay ? 3 : 0)
        + (f.SessionsSeen >= 2 ? 1 : 0)
        + (f.HasSaveFiles ? 2 : 0)
        - (f.OnlySettingsFiles ? 3 : 0)
        - (f.WrittenWhileOtherGameRan ? 4 : 0);

    /// <summary>A folder with no save files is never picked or asked about, whatever else points to it.</summary>
    public static SaveConfidence Of(SaveCandidateFacts f)
    {
        if (!f.HasSaveFiles) return SaveConfidence.Low;
        var points = Points(f);
        return points >= High ? SaveConfidence.High : points >= Medium ? SaveConfidence.Medium : SaveConfidence.Low;
    }
}

public static class SaveFileNames
{
    private static readonly string[] SettingsExtensions = [".ini", ".cfg", ".settings"];
    private static readonly string[] SettingsWords = ["config", "settings", "keybind", "options", "preferences"];
    private static readonly string[] SaveExtensions = [".sav", ".save", ".savegame", ".dat", ".bin", ".deadspacesaved", ".json"];

    public static bool IsSettings(string fileName)
    {
        var name = Path.GetFileName(fileName);
        return SettingsExtensions.Contains(Path.GetExtension(name), StringComparer.OrdinalIgnoreCase)
            || SettingsWords.Any(word => Path.GetFileNameWithoutExtension(name).Contains(word, StringComparison.OrdinalIgnoreCase));
    }

    public static bool IsSave(string path) =>
        IsRockstarSave(path) || (!IsSettings(path) && SaveExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase));

    /// <summary>Rockstar save files have no extension: SGTA400 (GTA IV), SGTA50000 (GTA V), SRDR30000 (Red Dead Redemption 2).</summary>
    public static bool IsRockstarSave(string path)
    {
        var name = Path.GetFileName(path);
        return name.Length is >= 7 and <= 12 && (name.StartsWith("SGTA", StringComparison.OrdinalIgnoreCase) || name.StartsWith("SRDR", StringComparison.OrdinalIgnoreCase))
            && name.Skip(4).All(char.IsAsciiDigit);
    }
}

public enum SaveBackupOutcome { BackedUp, WhenClosed, NotYet }

/// <summary>The short words of the save finder's notices.</summary>
public static class SaveFinderText
{
    public static string FoundTitle(string game) => game + ": saves found";
    public static string AskTitle(string game) => "Is this where " + game + " saves?";
    public static string MissingTitle(string game) => "Couldn't find " + game + "'s saves";

    /// <summary>A folder as the player knows it: without the long way to their user folder.</summary>
    public static string Short(string path, string home) =>
        home.Length > 0 && path.StartsWith(Path.TrimEndingDirectorySeparator(home) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            ? path[(Path.TrimEndingDirectorySeparator(home).Length + 1)..] : path;

    public static string FoundDetail(string path, string home, SaveBackupOutcome outcome) => "In " + Short(path, home) + outcome switch
    {
        SaveBackupOutcome.BackedUp => " · backed up",
        SaveBackupOutcome.WhenClosed => " · backs up when you close the game",
        _ => ""
    };
}

/// <summary>Names a game's folder may carry: its titles, program, publisher's product name and store number, letters and digits only.</summary>
public static class GameNameTokens
{
    private static readonly string[] Generic = ["game", "games", "documents", "savedgames", "saves", "save", "appdata", "public", "user", "users", "bin", "binaries", "win64", "windows", "steam", "launcher", "shipping"];

    public static IReadOnlySet<string> For(IEnumerable<string?> names) =>
        names.Select(n => Normalize(n ?? "")).Where(n => n.Length >= 3 && !Generic.Contains(n)).ToHashSet(StringComparer.Ordinal);

    public static bool Matches(string folderName, IReadOnlySet<string> tokens) => tokens.Contains(Normalize(folderName));

    public static string Normalize(string value) => new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
}

/// <summary>Writes that are never game saves: caches, logs, temporary files and other programs' folders.</summary>
public static class SaveActivityFilter
{
    private static readonly string[] Folders = ["Temp", "Microsoft", "CrashDumps", "D3DSCache", "NVIDIA", "NVIDIA Corporation", "AMD", "Google", "Mozilla", "BraveSoftware",
        "Opera Software", "discord", "htmlcache", "GameVault", "INetCache", "ShaderCache", "DXCache", "GLCache", "GPUCache"];
    private static readonly string[] Extensions = [".log", ".tmp", ".etl", ".dmp", ".lock", ".pdb", ".pf"];

    /// <summary>Judges only the part of the path below the watched folder. A path outside every watched folder is ignored.</summary>
    public static bool Ignore(string fullPath, IReadOnlyList<string> roots, IReadOnlySet<string>? tokens = null)
    {
        var root = SaveRootFinder.DeepestRoot(fullPath, roots);
        if (root is null) return true;
        var parts = fullPath[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return true;
        var name = parts[^1];
        if (Extensions.Contains(Path.GetExtension(name), StringComparer.OrdinalIgnoreCase) || name.EndsWith("-journal", StringComparison.OrdinalIgnoreCase)) return true;
        for (var i = 0; i < parts.Length - 1; i++)
        {
            if (Folders.Contains(parts[i], StringComparer.OrdinalIgnoreCase)) return true;
            // Microsoft Store games save under Packages\<their package>; every other package there is some other program.
            if (parts[i].Equals("Packages", StringComparison.OrdinalIgnoreCase)
                && !(tokens is not null && i + 1 < parts.Length - 1 && tokens.Any(t => GameNameTokens.Normalize(parts[i + 1]).Contains(t, StringComparison.Ordinal)))) return true;
        }
        return false;
    }
}

/// <summary>Turns files written during play into the folders that hold a game's saves.</summary>
public static class SaveRootFinder
{
    /// <summary>One folder per top-level branch under a protected root: the narrowest one holding that branch's save files (or all its writes when none
    /// look like saves). A protected root itself is never returned.</summary>
    public static IReadOnlyList<string> Lift(IEnumerable<string> writtenFiles, IReadOnlyList<string> protectedRoots)
    {
        var branches = new Dictionary<string, List<(string File, string Root)>>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in writtenFiles)
        {
            var root = DeepestRoot(file, protectedRoots);
            if (root is null) continue;
            var parts = file[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) continue;                       // a loose file directly in a protected root points at no folder
            var key = Path.Combine(root, parts[0]);
            if (!branches.TryGetValue(key, out var list)) branches[key] = list = [];
            list.Add((file, root));
        }
        var result = new List<string>();
        foreach (var branch in branches.Values)
        {
            var saves = branch.Where(f => SaveFileNames.IsSave(f.File)).ToList();
            var folders = (saves.Count > 0 ? saves : branch).Select(f => Path.GetDirectoryName(f.File)!).ToArray();
            var common = folders[0];
            foreach (var folder in folders) while (!Within(folder, common)) common = Path.GetDirectoryName(common)!;
            if (!protectedRoots.Any(r => Within(Trim(r), common))) result.Add(common);
        }
        result.Sort(StringComparer.OrdinalIgnoreCase);
        return result;
    }

    internal static string? DeepestRoot(string path, IReadOnlyList<string> roots) =>
        roots.Select(Trim).Where(r => Within(path, r) && path.Length > r.Length).MaxBy(r => r.Length);

    private static string Trim(string path) => Path.TrimEndingDirectorySeparator(path);

    private static bool Within(string path, string parent) =>
        path.Equals(parent, StringComparison.OrdinalIgnoreCase) || path.StartsWith(Trim(parent) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
}
