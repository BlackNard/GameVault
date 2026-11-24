namespace GameVault.Core;

public enum DropOutcome
{
    /// <summary>An executable was found; <see cref="DropResolution.ExecutablePath"/> is set.</summary>
    Accepted,
    /// <summary>Not an executable, a shortcut to one, or a folder (for example a document or an internet shortcut).</summary>
    Unsupported,
    /// <summary>A shortcut whose target is missing or is not an executable.</summary>
    BrokenShortcut,
    /// <summary>A folder with no likely game executable in it.</summary>
    NoExecutableFound,
    /// <summary>The executable is already a game in the library.</summary>
    AlreadyInLibrary
}

/// <param name="Outcome">What happened.</param>
/// <param name="ExecutablePath">The chosen executable for <see cref="DropOutcome.Accepted"/> and <see cref="DropOutcome.AlreadyInLibrary"/>.</param>
/// <param name="OtherCandidates">Other plausible executables found in a dropped folder, best first (never more than four).</param>
public sealed record DropResolution(DropOutcome Outcome, string? ExecutablePath = null, IReadOnlyList<string>? OtherCandidates = null)
{
    public IReadOnlyList<string> Others => OtherCandidates ?? [];
}

/// <summary>
/// Decides what a dropped file or folder means for "add a game": an .exe, a .lnk shortcut to one, or a folder that holds one. It only reads
/// names (and asks the caller to list a folder one level deep); nothing is run, copied or opened. All file access is injected so the rules are testable.
/// </summary>
public static class DroppedGameResolver
{
    private static readonly string[] NotTheGame =
    [
        "unins", "uninst", "setup", "install", "redist", "vcredist", "vc_redist", "dxsetup", "dotnet", "directx", "oalinst", "physx",
        "crashhandler", "crashreport", "crashpad", "crashsender", "unitycrashhandler", "bugreport", "errorreport", "updater", "patcher",
        "eac", "easyanticheat", "battleye", "beclient", "benchmark", "ue4prereq", "ueprereq", "quicksfv", "dxwebsetup", "7z", "notification_helper"
    ];

    public const int MaxOtherCandidates = 4;

    public static DropResolution Resolve(
        string dropped,
        Func<string, string?> shortcutTarget,
        Func<string, bool> fileExists,
        Func<string, bool> directoryExists,
        Func<string, IEnumerable<string>> executablesIn,
        Func<string, IEnumerable<string>> subfoldersOf,
        Func<string, bool> alreadyInLibrary)
    {
        string path;
        try { path = Path.GetFullPath(dropped.Trim().Trim('"')); }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException) { return new(DropOutcome.Unsupported); }

        var extension = Path.GetExtension(path);
        if (extension.Equals(".exe", StringComparison.OrdinalIgnoreCase))
            return fileExists(path) ? Classify(path, alreadyInLibrary) : new(DropOutcome.Unsupported);

        if (extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase))
        {
            string? target;
            try { target = shortcutTarget(path); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException or InvalidCastException) { target = null; }
            if (string.IsNullOrWhiteSpace(target)) return new(DropOutcome.BrokenShortcut);
            string resolved;
            try { resolved = Path.GetFullPath(target.Trim().Trim('"')); }
            catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException) { return new(DropOutcome.BrokenShortcut); }
            return Path.GetExtension(resolved).Equals(".exe", StringComparison.OrdinalIgnoreCase) && fileExists(resolved)
                ? Classify(resolved, alreadyInLibrary)
                : new(DropOutcome.BrokenShortcut);
        }

        if (extension.Length == 0 && directoryExists(path)) return FromFolder(path, executablesIn, subfoldersOf, alreadyInLibrary);
        return new(DropOutcome.Unsupported);
    }

    private static DropResolution Classify(string executable, Func<string, bool> alreadyInLibrary) =>
        new(alreadyInLibrary(executable) ? DropOutcome.AlreadyInLibrary : DropOutcome.Accepted, executable);

    private static DropResolution FromFolder(string folder, Func<string, IEnumerable<string>> executablesIn, Func<string, IEnumerable<string>> subfoldersOf, Func<string, bool> alreadyInLibrary)
    {
        var folderName = Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var candidates = new List<(string Path, int Depth)>();
        foreach (var exe in Safe(() => executablesIn(folder))) candidates.Add((exe, 0));
        foreach (var sub in Safe(() => subfoldersOf(folder)))
            foreach (var exe in Safe(() => executablesIn(sub))) candidates.Add((exe, 1));

        var ranked = candidates
            .Where(c => !LooksLikeHelper(System.IO.Path.GetFileNameWithoutExtension(c.Path)))
            .OrderByDescending(c => Similarity(System.IO.Path.GetFileNameWithoutExtension(c.Path), folderName))
            .ThenBy(c => c.Depth)
            .ThenBy(c => System.IO.Path.GetFileName(c.Path).Length)
            .ThenBy(c => c.Path, StringComparer.OrdinalIgnoreCase)
            .Select(c => c.Path)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (ranked.Length == 0) return new(DropOutcome.NoExecutableFound);
        var chosen = Classify(ranked[0], alreadyInLibrary);
        return chosen with { OtherCandidates = [.. ranked.Skip(1).Take(MaxOtherCandidates)] };
    }

    private static IEnumerable<string> Safe(Func<IEnumerable<string>> list)
    {
        try { return list().ToArray(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return []; }
    }

    private static bool LooksLikeHelper(string name)
    {
        var lowered = name.ToLowerInvariant();
        return NotTheGame.Any(word => lowered.Contains(word, StringComparison.Ordinal));
    }

    /// <summary>0 to 3: how closely an executable's name matches the folder it sits in (exact, contains, shares letters-and-digits, nothing).</summary>
    private static int Similarity(string exeName, string folderName)
    {
        var a = Squash(exeName);
        var b = Squash(folderName);
        if (a.Length == 0 || b.Length == 0) return 0;
        if (a == b) return 3;
        if (a.Contains(b, StringComparison.Ordinal) || b.Contains(a, StringComparison.Ordinal)) return 2;
        return a.Length >= 4 && b.StartsWith(a[..4], StringComparison.Ordinal) ? 1 : 0;
    }

    private static string Squash(string value) => new([.. value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant)]);
}
