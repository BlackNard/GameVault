using GameVault.Core;
using GameVault.Infrastructure.Storage;

namespace GameVault.Infrastructure.Library;

public sealed record StatisticsData(GamePlay[] Games, bool HasSteamTime, string[] Warnings);

/// <summary>Reads every game's recorded play, removed games included, for the Statistics page. Nothing is written.</summary>
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
                // Session journals are not read: the figures come from the recorded sessions, which stays quick with years of play.
                var history = await store.LoadHistoryAsync(game.Id, includeJournals: false).ConfigureAwait(false);
                if (history.Game.Statistics.Value is null) { warnings.Add($"{profile.Name}: its recorded playtime could not be read, so it is not counted."); continue; }
                var linked = (await steam.GetAsync(game.Id).ConfigureAwait(false)).Totals?.SteamSeconds ?? 0;
                hasSteam |= linked > 0;
                var play = await Task.Run(() => PlayStatistics.FromHistory(profile.Name, profile.IsArchived, history, zone, linked)).ConfigureAwait(false);
                games.Add(play);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or OverflowException or InvalidOperationException)
            { warnings.Add($"{profile.Name}: not counted. {error.Message}"); }
        }
        return new([.. games], hasSteam, [.. warnings.Distinct()]);
    }
}
