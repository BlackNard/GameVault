using System.Globalization;
using System.Text;
using GameVault.Core;
using GameVault.Infrastructure.Storage;

namespace GameVault.Infrastructure.Library;

public sealed record CsvExportResult(string Folder, int Games, string[] Warnings);

/// <summary>Writes the library as spreadsheet files into a new folder. No program paths or account identifiers are included; nothing is overwritten.</summary>
public sealed class CsvExportService(AppDataStore store)
{
    public async Task<CsvExportResult> ExportAsync(string destination, TimeZoneInfo zone, DateTime now)
    {
        if (string.IsNullOrWhiteSpace(destination) || !Path.IsPathFullyQualified(destination) || !Directory.Exists(destination))
            throw new IOException("Choose an existing folder for the export.");
        var snapshot = await store.InitializeAsync().ConfigureAwait(false);
        var warnings = new List<string>(snapshot.Warnings);
        var tags = (await store.LoadLibraryTagsAsync().ConfigureAwait(false)).Value;
        var lists = (await store.LoadListsAsync().ConfigureAwait(false)).Value ?? ListsDocument.Empty;
        var games = new List<string?[]>();
        var sessions = new List<string?[]>();
        var runs = new List<string?[]>();
        var journal = new List<string?[]>();
        var names = new Dictionary<Guid, string>();
        static string Day(DateOnly? date) => date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "";
        static string Hours(long seconds) => (seconds / 3600.0).ToString("0.00", CultureInfo.InvariantCulture);
        static string Yes(bool value) => value ? "yes" : "no";
        string Local(DateTimeOffset at) => TimeZoneInfo.ConvertTime(at, zone).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        foreach (var game in snapshot.Games.OrderBy(g => g.Profile.Value?.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            if (game.Profile.Value is not { } profile) { warnings.Add("A game's profile could not be read, so it is not included."); continue; }
            names[game.Id] = profile.Name;
            var statistics = game.Statistics.Value;
            var detail = (await store.LoadStatusDetailAsync(game.Id).ConfigureAwait(false)).Value?.Detail;
            var stars = (await store.LoadRatingAsync(game.Id).ConfigureAwait(false)).Value?.HalfStars ?? 0;
            var completed = (await store.LoadCompletionAsync(game.Id).ConfigureAwait(false)).Value?.CompletedAt;
            var genres = (await store.LoadMetadataAsync(game.Id).ConfigureAwait(false)).Value?.Get(MetadataField.Genres)?.Items;
            var played = statistics is null ? 0 : statistics.Sessions.Sum(s => s.DurationSeconds) + statistics.Adjustments.Sum(a => a.Seconds);
            games.Add([profile.Name, PlayStatuses.Label(PlayStatuses.From(profile.Status, detail)), stars > 0 ? (stars / 2.0).ToString("0.#", CultureInfo.InvariantCulture) : "",
                statistics is null ? "" : Hours(Math.Max(0, played)), statistics?.Sessions.Length.ToString(CultureInfo.InvariantCulture) ?? "",
                statistics is { Sessions.Length: > 0 } ? Local(statistics.Sessions.Max(s => s.StartedAt))[..10] : "",
                completed is { } done ? Local(done)[..10] : "", Yes(lists.Favourites.Contains(game.Id)),
                string.Join("; ", tags?.Tags.GetValueOrDefault(game.Id) ?? []), string.Join("; ", genres ?? []), Yes(profile.IsArchived)]);
            foreach (var session in (statistics?.Sessions ?? []).OrderBy(s => s.StartedAt))
                sessions.Add([profile.Name, Local(session.StartedAt), session.EndedAt is { } end ? Local(end) : "", (session.DurationSeconds / 60).ToString(CultureInfo.InvariantCulture)]);
            foreach (var run in (await store.LoadPlaythroughsAsync(game.Id).ConfigureAwait(false)).Value?.Runs ?? [])
                runs.Add([profile.Name, run.Title, run.Platform, Day(run.Started), Day(run.Finished), run.Outcome.ToString(), run.Minutes is { } m ? Hours(m * 60L) : "",
                    run.HalfStars > 0 ? (run.HalfStars / 2.0).ToString("0.#", CultureInfo.InvariantCulture) : "", Yes(run.Mastered), run.Review]);
            foreach (var entry in ((await store.LoadJournalAsync(game.Id).ConfigureAwait(false)).Value?.Entries ?? []).OrderBy(e => e.Date))
                journal.Add([profile.Name, Day(entry.Date), entry.Minutes?.ToString(CultureInfo.InvariantCulture), entry.Note, string.Join("; ", entry.Tags)]);
        }
        var listRows = lists.Lists.SelectMany(l => l.Entries.Select((e, i) => new[]
        { l.Name, Yes(l.Ranked), (i + 1).ToString(CultureInfo.InvariantCulture), e.GameId is { } id && names.TryGetValue(id, out var name) ? name : e.Title, e.Note })).ToArray();

        return await Task.Run(() =>
        {
            var target = Path.GetFullPath(destination);
            store.RequireOutsideData(target);
            var folder = Path.Combine(target, "GameVault-export-" + now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture));
            for (var number = 2; Directory.Exists(folder) || File.Exists(folder); number++)
                folder = Path.Combine(target, $"GameVault-export-{now:yyyyMMdd-HHmmss}-{number}");
            Directory.CreateDirectory(folder);
            void Write(string file, IEnumerable<string> header, IEnumerable<string?[]> rows) =>
                File.WriteAllText(Path.Combine(folder, file), Csv.Table(header, rows), new UTF8Encoding(true));
            Write("games.csv", ["Name", "Status", "Rating", "Hours played", "Sessions", "Last played", "Completed on", "Favourite", "Tags", "Genres", "Removed"], games);
            Write("sessions.csv", ["Game", "Started", "Ended", "Minutes"], sessions);
            Write("playthroughs.csv", ["Game", "Title", "Platform", "Started", "Finished", "Outcome", "Hours typed", "Rating", "Mastered", "Review"], runs);
            Write("journal.csv", ["Game", "Date", "Minutes", "Note", "Tags"], journal);
            Write("lists.csv", ["List", "Ranked", "Position", "Game", "Note"], listRows);
            return new CsvExportResult(folder, games.Count, [.. warnings.Distinct()]);
        }).ConfigureAwait(false);
    }
}
