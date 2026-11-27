using System.Diagnostics;
using GameVault.Core;

namespace GameVault.Infrastructure.Saves;

/// <summary>Every folder worth looking at for a game's saves (issue #111): the known rules, the places its store number and publisher point to,
/// folders named after it, and folders written while it ran. Only folders that exist are added beyond the known rules.</summary>
public static class SaveFinder
{
    private const int NameSearchDepth = 3, FoldersPerRoot = 2000, MaxNameMatches = 16;
    private static readonly TimeSpan NameSearchTime = TimeSpan.FromSeconds(1);

    private enum Base { Roaming, Public, ProgramData }

    /// <summary>Folders that Steam replacement setups keep saves in, each followed by the app number (and often <c>remote</c>).</summary>
    private static readonly (Base Root, string Folder)[] StoreNumberFolders =
    [
        (Base.Roaming, "Goldberg SteamEmu Saves"), (Base.Roaming, "GSE Saves"), (Base.Roaming, "HOODLUM"), (Base.Roaming, "EMPRESS"), (Base.Roaming, "FLT"),
        (Base.Roaming, "CODEX"), (Base.Roaming, "RUNE"), (Base.Roaming, "TENOKE"), (Base.Roaming, "OnlineFix"),
        (Base.Public, @"Steam\CODEX"), (Base.Public, @"Steam\RUNE"), (Base.Public, "OnlineFix"), (Base.Public, "CPY_SAVES"),
        (Base.ProgramData, @"Steam\RLD!")
    ];

    public static SaveCandidateCatalog Candidates(GameProfile profile, GameSaveIdentity identity, SaveDiscoveryEnvironment env, SaveFindsDocument? finds, CancellationToken token)
    {
        var rules = SaveCandidateRules.For(profile, env, identity.SteamAppId);
        var candidates = rules.Candidates.ToList();

        if (identity.SteamAppId is { } number)
        {
            var words = $"Steam app {number} folder";
            foreach (var (place, folder) in StoreNumberFolders)
            {
                var root = place switch { Base.Roaming => env.Roaming, Base.Public => env.PublicDocuments, _ => env.ProgramData };
                var game = Path.Combine(root, folder, number);
                Existing(Path.Combine(game, "remote"), SaveCandidateSource.StoreNumber, words);
                Existing(game, SaveCandidateSource.StoreNumber, words);
            }
            foreach (var steam in env.SteamRoots)
            {
                try
                {
                    var users = Path.Combine(steam, "userdata");
                    if (!Directory.Exists(users)) continue;
                    foreach (var account in Directory.EnumerateDirectories(users).Where(a => Path.GetFileName(a).All(char.IsAsciiDigit)).Take(32))
                        Existing(Path.Combine(account, number, "remote"), SaveCandidateSource.StoreNumber, words);
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
            }
        }

        string[] userRoots = [env.Roaming, env.Local, env.LocalLow, env.Documents, Path.Combine(env.Documents, "My Games"), env.SavedGames];
        if (identity.Product is { } product)
            foreach (var root in userRoots)
            {
                if (identity.Company is { } company) Existing(Path.Combine(root, company, product), SaveCandidateSource.PublisherFolder, "Publisher's game folder");
                // A product name shared by many programs (an engine's default) says nothing on its own.
                if (GameNameTokens.Matches(product, identity.Tokens)) Existing(Path.Combine(root, product), SaveCandidateSource.PublisherFolder, "Folder named like the program");
            }

        var (named, _) = SearchByName([env.Roaming, env.Local, env.LocalLow, env.Documents, env.SavedGames, env.PublicDocuments], identity.Tokens, NameSearchTime, token);
        foreach (var folder in named) Existing(folder, SaveCandidateSource.NameSearch, "Folder named after the game");

        foreach (var find in finds?.Finds ?? []) Existing(find.Path, SaveCandidateSource.Activity, "Written while you played");

        return new([.. candidates], rules.Warnings);

        void Existing(string path, SaveCandidateSource source, string evidence)
        {
            try { if (Directory.Exists(path)) candidates.Add(new(path, SaveSourceOrigin.Suggestion, evidence, "any", false, source)); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException) { }
        }
    }

    /// <summary>A shallow, bounded look for folders named after the game: at most <c>2,000</c> folders per root, three levels deep, within the time
    /// budget; links and cache folders are not entered.</summary>
    public static (IReadOnlyList<string> Found, int Visited) SearchByName(IReadOnlyList<string> roots, IReadOnlySet<string> tokens, TimeSpan budget, CancellationToken token)
    {
        var found = new List<string>();
        var visited = 0;
        if (tokens.Count == 0) return (found, visited);
        var clock = Stopwatch.StartNew();
        foreach (var root in roots.Where(r => !string.IsNullOrWhiteSpace(r)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var inRoot = 0;
            var queue = new Queue<(string Path, int Depth)>();
            queue.Enqueue((root, 0));
            while (inRoot < FoldersPerRoot && found.Count < MaxNameMatches && clock.Elapsed < budget && queue.TryDequeue(out var item))
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    foreach (var child in new DirectoryInfo(item.Path).EnumerateDirectories())
                    {
                        if (inRoot >= FoldersPerRoot || clock.Elapsed >= budget) break;
                        inRoot++;
                        // Asked about a file inside the child, so the child's own name is judged as a folder.
                        if ((child.Attributes & FileAttributes.ReparsePoint) != 0 || SaveActivityFilter.Ignore(Path.Combine(child.FullName, "x"), [root], tokens)) continue;
                        if (GameNameTokens.Matches(child.Name, tokens)) found.Add(child.FullName);
                        else if (item.Depth + 1 < NameSearchDepth) queue.Enqueue((child.FullName, item.Depth + 1));
                    }
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
            }
            visited = Math.Max(visited, inRoot);
        }
        return (found.Distinct(StringComparer.OrdinalIgnoreCase).Take(MaxNameMatches).ToArray(), visited);
    }
}
