using GameVault.Core;
using GameVault.Infrastructure.Storage;

namespace GameVault.Infrastructure.Library;

public sealed record StatisticsData(GamePlay[] Games, bool HasSteamTime, string[] Warnings);

/// <summary>Reads every game's recorded play, removed games included, for the statistics. Nothing is written.</summary>
public sealed class StatisticsService(AppDataStore store, SteamHoursService steam)
{
    public async Task<StatisticsData> LoadAsync(TimeZoneInfo zone)
    {
        var snapshot = await store.InitializeAsync().ConfigureAwait(false);
        var games = new List<GamePlay>();
        var warnings = new List<string>();
        var hasSteam = false;
        foreach (var game in snapshot.Games)
        {
            if (game.Profile.Value is not { } profile) { warnings.Add("A game's profile could not be read, so it is not counted."); continue; }
            try
            {
                var (play, steamSeconds) = await ReadAsync(profile, zone).ConfigureAwait(false);
                if (play is null) { warnings.Add($"{profile.Name}: its recorded playtime could not be read, so it is not counted."); continue; }
                hasSteam |= steamSeconds > 0;
                games.Add(play);
            }
            catch (Exception error) when (IsReadError(error)) { warnings.Add($"{profile.Name}: not counted. {error.Message}"); }
        }
        return new([.. games], hasSteam, [.. warnings.Distinct()]);
    }

    /// <summary>One game's play, or null when it cannot be read.</summary>
    public async Task<GamePlay?> LoadGameAsync(Guid id, TimeZoneInfo zone)
    {
        var profile = (await store.LoadProfileAsync(id).ConfigureAwait(false)).Profile.Value;
        return profile is null ? null : (await ReadAsync(profile, zone).ConfigureAwait(false)).Play;
    }

    private async Task<(GamePlay? Play, long SteamSeconds)> ReadAsync(GameProfile profile, TimeZoneInfo zone)
    {
        var id = profile.Id;
        // Session journals are not read: the figures come from the recorded sessions, which stays quick with years of play.
        var history = await store.LoadHistoryAsync(id, includeJournals: false).ConfigureAwait(false);
        if (history.Game.Statistics.Value is null) return (null, 0);
        var linked = (await steam.GetAsync(id).ConfigureAwait(false)).Totals?.SteamSeconds ?? 0;
        var genres = (await store.LoadMetadataAsync(id).ConfigureAwait(false)).Value?.Get(MetadataField.Genres)?.Items;
        var completed = (await store.LoadCompletionAsync(id).ConfigureAwait(false)).Value?.CompletedAt;
        var stars = (await store.LoadRatingAsync(id).ConfigureAwait(false)).Value?.HalfStars ?? 0;
        var play = await Task.Run(() => PlayStatistics.FromHistory(profile.Name, profile.IsArchived, history, zone, linked)).ConfigureAwait(false);
        return (play with
        {
            Genres = genres, Status = profile.Status, HalfStars = stars,
            CompletedOn = completed is { } at ? DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(at, zone).DateTime) : null
        }, linked);
    }

    private static bool IsReadError(Exception error) => error is IOException or UnauthorizedAccessException or OverflowException or InvalidOperationException;
}
