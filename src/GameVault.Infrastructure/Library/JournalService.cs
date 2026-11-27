using GameVault.Core;
using GameVault.Infrastructure.Storage;

namespace GameVault.Infrastructure.Library;

public sealed record JournalFeed(JournalItem[] Items, (Guid Id, string Name, bool Removed)[] Games, string[] Warnings);

/// <summary>Gathers the entries added by hand: those without a game and every game's, removed games included. Nothing is written.</summary>
public sealed class JournalService(AppDataStore store)
{
    public async Task<JournalFeed> LoadAsync()
    {
        var snapshot = await store.InitializeAsync().ConfigureAwait(false);
        var items = new List<JournalItem>();
        var games = new List<(Guid, string, bool)>();
        var warnings = new List<string>();
        var general = await store.LoadGeneralJournalAsync().ConfigureAwait(false);
        if (general.Message is { } problem) warnings.Add(problem);
        items.AddRange((general.Value?.Entries ?? []).Select(e => new JournalItem(Guid.Empty, null, e)));
        foreach (var game in snapshot.Games)
        {
            if (game.Profile.Value is not { } profile) { warnings.Add("A game's profile could not be read, so it is not listed."); continue; }
            games.Add((game.Id, profile.Name, profile.IsArchived));
            try
            {
                var journal = await store.LoadJournalAsync(game.Id).ConfigureAwait(false);
                if (journal.Message is { } message) warnings.Add($"{profile.Name}: {message}");
                items.AddRange((journal.Value?.Entries ?? []).Select(e => new JournalItem(game.Id, profile.Name, e)));
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException) { warnings.Add($"{profile.Name}: not listed. {error.Message}"); }
        }
        return new([.. items], [.. games.OrderBy(g => g.Item2, StringComparer.CurrentCultureIgnoreCase)], [.. warnings.Distinct()]);
    }
}
