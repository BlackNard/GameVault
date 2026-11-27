using GameVault.Core;
using GameVault.Infrastructure.Storage;

namespace GameVault.Infrastructure.Library;

public sealed record JournalFeed(JournalItem[] Items, (Guid Id, string Name, bool Removed)[] Games, string[] Warnings);

/// <summary>Gathers every game's sessions, their notes and the entries added by hand, removed games included. Nothing is written.</summary>
public sealed class JournalService(AppDataStore store)
{
    public async Task<JournalFeed> LoadAsync(TimeZoneInfo zone)
    {
        var snapshot = await store.InitializeAsync().ConfigureAwait(false);
        var items = new List<JournalItem>();
        var games = new List<(Guid, string, bool)>();
        var warnings = new List<string>();
        foreach (var game in snapshot.Games)
        {
            if (game.Profile.Value is not { } profile) { warnings.Add("A game's profile could not be read, so it is not listed."); continue; }
            games.Add((game.Id, profile.Name, profile.IsArchived));
            try
            {
                var notes = (await store.LoadSessionNoteDocumentsAsync(game.Id).ConfigureAwait(false)).Where(n => n.SessionId is not null).ToDictionary(n => n.SessionId!.Value);
                var journal = await store.LoadJournalAsync(game.Id).ConfigureAwait(false);
                var details = (journal.Value?.Sessions ?? []).ToDictionary(s => s.SessionId);
                foreach (var session in game.Statistics.Value?.Sessions ?? [])
                {
                    var note = notes.GetValueOrDefault(session.Id);
                    if (session.DurationSeconds <= 0 && note is null) continue;
                    items.Add(new(TimeZoneInfo.ConvertTime(session.StartedAt, zone).DateTime, game.Id, profile.Name, session.DurationSeconds, note?.Title, note?.Body, details.GetValueOrDefault(session.Id)?.Tags ?? [], session.Id, null));
                }
                if (journal.Message is { } message) warnings.Add($"{profile.Name}: {message}");
                foreach (var entry in journal.Value?.Entries ?? [])
                    items.Add(new(entry.Date.ToDateTime(TimeOnly.MinValue), game.Id, profile.Name, entry.Minutes * 60L, entry.Title, entry.Note, entry.Tags, null, entry.Id));
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException) { warnings.Add($"{profile.Name}: not listed. {error.Message}"); }
        }
        return new([.. items], [.. games.OrderBy(g => g.Item2, StringComparer.CurrentCultureIgnoreCase)], [.. warnings.Distinct()]);
    }
}
