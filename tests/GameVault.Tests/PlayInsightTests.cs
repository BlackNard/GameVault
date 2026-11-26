using GameVault.Core;
using GameVault.Infrastructure.Library;
using GameVault.Infrastructure.Storage;

namespace GameVault.Tests;

/// <summary>Statistics, second stage (ticket #61): patterns, shares, session and completion figures, calendar and year in review.</summary>
public sealed class PlayInsightTests
{
    private static DateOnly D(int year, int month, int day) => new(year, month, day);

    private static GamePlay Game(string name, string[]? genres = null, GameStatus status = GameStatus.Playing, DateOnly? completed = null,
        params (DateTime Start, long Seconds)[] sessions)
    {
        var spans = sessions.Select(s => new PlaySpan(s.Start, s.Seconds)).ToArray();
        var days = spans.GroupBy(s => DateOnly.FromDateTime(s.Start)).Select(g => new ActivityBucket(g.Key, g.Sum(s => s.Seconds))).ToArray();
        return new(Guid.NewGuid(), name, false, days, [.. spans.Select(s => DateOnly.FromDateTime(s.Start))], 0, spans, genres, status, completed);
    }

    [Fact]
    public void SessionFiguresCoverThePeriodOnly()
    {
        var a = Game("A", completed: D(2026, 11, 20), sessions: [(new(2026, 10, 1, 10, 0, 0), 5000), (new(2026, 11, 2, 10, 0, 0), 600)]);
        var b = Game("B", sessions: [(new(2026, 11, 3, 10, 0, 0), 1800)]);
        var month = PlayStatistics.Insight([a, b], (D(2026, 11, 1), D(2026, 11, 30)));
        Assert.Equal((1200L, 1800L, "B", D(2026, 11, 3)), (month.AverageSeconds, month.LongestSeconds, month.LongestGame, month.LongestOn!.Value));
        Assert.Equal(1, month.NewGames);                         // A was started in October
        Assert.Equal(["A"], month.Completed);
        var all = PlayStatistics.Insight([a, b], null);
        Assert.Equal((2, 5000L), (all.NewGames, all.LongestSeconds));
        var none = PlayStatistics.Insight([a, b], (D(2020, 1, 1), D(2020, 12, 31)));
        Assert.Equal((0L, 0L, null, 0), (none.AverageSeconds, none.LongestSeconds, none.LongestGame, none.Completed.Length));
    }

    [Fact]
    public void TheAverageRatingCoversRatedGamesPlayedInThePeriod()
    {
        var a = Game("A", sessions: [(new(2026, 11, 1, 10, 0, 0), 60)]) with { HalfStars = 8 };
        var b = Game("B", sessions: [(new(2026, 11, 2, 10, 0, 0), 60)]) with { HalfStars = 5 };
        var unrated = Game("C", sessions: [(new(2026, 11, 3, 10, 0, 0), 60)]);
        var old = Game("D", sessions: [(new(2025, 1, 1, 10, 0, 0), 60)]) with { HalfStars = 2 };
        Assert.Equal(3.25, PlayStatistics.AverageStars([a, b, unrated, old], (D(2026, 11, 1), D(2026, 11, 30))));
        Assert.Null(PlayStatistics.AverageStars([unrated], null));
    }

    [Fact]
    public void TimeOfDaySplitsASessionOverTheHoursItCovered()
    {
        var game = Game("A", sessions: [(new(2026, 11, 27, 23, 30, 0), 5400)]);      // Friday 23:30 for 90 minutes
        var hours = PlayStatistics.Hours([game], null);
        Assert.Equal(24, hours.Length);
        Assert.Equal((1800L, 3600L, 5400L), (hours[23], hours[0], hours.Sum()));
        var weekdays = PlayStatistics.Weekdays([game], null);
        Assert.Equal(5400, weekdays[(int)DayOfWeek.Friday]);
        Assert.Equal(0, PlayStatistics.Hours([game], (D(2026, 1, 1), D(2026, 1, 31))).Sum());
    }

    [Fact]
    public void TheCalendarCoversWholeWeeksUpToTodayWithEmptyDays()
    {
        var game = Game("A", sessions: [(new(2026, 11, 27, 10, 0, 0), 100)]);
        var days = PlayStatistics.Calendar([game], D(2026, 11, 27), DayOfWeek.Monday, 53);
        Assert.Equal(D(2025, 11, 24), days[0].Date);
        Assert.Equal(DayOfWeek.Monday, days[0].Date.DayOfWeek);
        Assert.Equal((D(2026, 11, 27), 100L), (days[^1].Date, days[^1].Seconds));
        Assert.Equal(52 * 7 + 5, days.Length);
        Assert.Equal([0, 1, 1, 2, 3, 4, 4], new long[] { 0, 1, 25, 26, 75, 76, 100 }.Select(s => PlayStatistics.Level(s, 100)));
    }

    [Fact]
    public void SharesAddUpAndSplitAGameBetweenItsGenres()
    {
        var a = Game("A", ["Action", "Horror"], GameStatus.Completed, sessions: [(new(2026, 11, 1, 10, 0, 0), 600)]);
        var b = Game("B", ["Action"], sessions: [(new(2026, 11, 2, 10, 0, 0), 300)]);
        var c = Game("C", sessions: [(new(2026, 11, 3, 10, 0, 0), 100)]);
        var genres = PlayStatistics.GenreShares([a, b, c], null);
        Assert.Equal([("Action", 600L), ("Horror", 300L), ("Unknown genre", 100L)], genres.Select(s => (s.Name, s.Seconds)));
        Assert.Equal(1.0, genres.Sum(s => s.Fraction), 6);
        var statuses = PlayStatistics.StatusShares([a, b, c], null);
        Assert.Equal([("Completed", 600L), ("Playing", 400L)], statuses.Select(s => (s.Name, s.Seconds)));
        var games = Enumerable.Range(1, 8).Select(i => Game("G" + i, sessions: [(new(2026, 11, 1, 10, 0, 0), i * 10)])).ToArray();
        var shares = PlayStatistics.GameShares(games, null, 6);
        Assert.Equal(7, shares.Length);
        Assert.Equal(("Other games", 30L), (shares[^1].Name, shares[^1].Seconds));
        Assert.Empty(PlayStatistics.GameShares([], null, 6));
    }

    [Fact]
    public void TheYearInReviewSumsOneYear()
    {
        var a = Game("A", ["Horror"], completed: D(2026, 3, 9), sessions:
            [(new(2025, 12, 31, 10, 0, 0), 9000), (new(2026, 3, 7, 10, 0, 0), 100), (new(2026, 3, 8, 10, 0, 0), 200), (new(2026, 3, 9, 10, 0, 0), 300)]);
        var b = Game("B", ["Action"], sessions: [(new(2026, 5, 1, 10, 0, 0), 400)]);
        var review = PlayStatistics.Review([a, b], 2026);
        Assert.Equal((1000L, 4, 2, 4), (review.Stats.Seconds, review.Stats.Sessions, review.Stats.Games, review.Stats.DaysPlayed));
        Assert.Equal(3, review.LongestStreak);
        Assert.Equal((D(2026, 3, 1), 600L), (review.BusiestMonth!.Value, review.BusiestSeconds));
        Assert.Equal("Horror", review.TopGenre);
        Assert.Equal(1, review.Insight.NewGames);               // only B was first played in 2026
        Assert.Equal(["A"], review.Insight.Completed);
        Assert.Null(PlayStatistics.Review([a, b], 2019).BusiestMonth);
    }

    [Fact]
    public async Task ACompletionDateIsRecordedWhenAGameBecomesCompletedAndForgottenIfThatIsUndone()
    {
        var root = Path.Combine(Path.GetTempPath(), "GameVault completion", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "game"));
            var exe = Path.Combine(root, "game", "game.exe");
            File.WriteAllText(exe, "fixture only; never executed");
            var paths = ApplicationPaths.FromRoot(Path.Combine(root, "app"));
            Directory.CreateDirectory(paths.Root);
            var store = new AppDataStore(paths);
            var library = new LibraryService(paths, store);
            var folder = Path.GetDirectoryName(exe)!;
            var id = (await library.SaveAsync(new(null, "Finisher", exe, folder, []))).Profile.Id;
            Assert.Null((await store.LoadCompletionAsync(id)).Value);
            await store.SetGameStatusAsync(id, GameStatus.Completed);
            var first = (await store.LoadCompletionAsync(id)).Value!.CompletedAt;
            Assert.True(DateTimeOffset.UtcNow - first < TimeSpan.FromMinutes(5));
            await library.SaveAsync(new(id, "Finisher renamed", exe, folder, [], GameStatus.Completed));   // still completed: the date stays
            Assert.Equal(first, (await store.LoadCompletionAsync(id)).Value!.CompletedAt);
            await library.SaveAsync(new(id, "Finisher", exe, folder, [], GameStatus.Playing));
            Assert.Equal(DocumentState.Missing, (await store.LoadCompletionAsync(id)).State);
            await library.SaveAsync(new(id, "Finisher", exe, folder, [], GameStatus.Completed));           // through the editor too
            Assert.NotNull((await store.LoadCompletionAsync(id)).Value);

            // A game added as already completed has no known date.
            var other = Path.Combine(root, "game", "other.exe");
            File.WriteAllText(other, "fixture");
            var done = (await library.SaveAsync(new(null, "Old win", other, folder, [], GameStatus.Completed))).Profile.Id;
            Assert.Null((await store.LoadCompletionAsync(done)).Value);

            var steam = new SteamHoursService(store, new SteamLocalData(null), new GameVault.Infrastructure.Launching.StoreGameLauncher(Path.Combine(root, "none")),
                (_, _) => Task.FromResult<SteamAppSummary?>(null));
            await store.SaveMetadataAsync(GameMetadataDocument.Empty(id) with
            { Fields = new() { [nameof(MetadataField.Genres)] = new("owner", true, DateTimeOffset.UtcNow, Items: ["Puzzle"]) } });
            await store.SaveRatingAsync(id, 9);
            var read = (await new StatisticsService(store, steam).LoadAsync(TimeZoneInfo.Utc)).Games.Single(g => g.GameId == id);
            var single = await new StatisticsService(store, steam).LoadGameAsync(id, TimeZoneInfo.Utc);
            Assert.Equal((id, 9), (single!.GameId, single.HalfStars));
            Assert.Equal(GameStatus.Completed, read.Status);
            Assert.Equal(["Puzzle"], read.Genres!);
            Assert.NotNull(read.CompletedOn);
        }
        finally { try { Directory.Delete(root, true); } catch (IOException) { } }
    }
}
