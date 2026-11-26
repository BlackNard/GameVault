using GameVault.Core;
using GameVault.Infrastructure.Library;
using GameVault.Infrastructure.Storage;

namespace GameVault.Tests;

/// <summary>Playtime statistics (ticket #61, first stage), on synthetic sessions only.</summary>
public sealed class PlayStatisticsTests
{
    private static DateOnly D(int year, int month, int day) => new(year, month, day);

    private static GamePlay Game(string name, long undated, params (DateOnly Date, long Seconds)[] days) => new(Guid.NewGuid(), name, false,
        [.. days.Select(d => new ActivityBucket(d.Date, d.Seconds))], [.. days.Select(d => d.Date)], undated);

    private static HistoryData History(params PlaySession[] sessions)
    {
        var id = Guid.NewGuid();
        var profile = new GameProfile(1, id, "Game", @"C:\g\game.exe", @"C:\g", [], GameStatus.Playing, 20, false, false);
        return new(new(id, new(profile, DocumentState.Ready), new(new GameStatistics(1, id, sessions, [new(Guid.NewGuid(), 500, "typed")]), DocumentState.Ready)), [], [], []);
    }

    private static PlaySession Session(DateTimeOffset start, int seconds) => new(Guid.NewGuid(), start, start.AddSeconds(seconds), seconds);

    [Fact]
    public void ASessionAcrossMidnightIsSplitAndCountedOnceOnTheDayItStarted()
    {
        var zone = TimeZoneInfo.CreateCustomTimeZone("plus2", TimeSpan.FromHours(2), "plus2", "plus2");
        var game = PlayStatistics.FromHistory("Game", false, History(Session(new(2026, 3, 1, 21, 0, 0, TimeSpan.Zero), 7200)), zone, 0);
        Assert.Equal([(D(2026, 3, 1), 3600L), (D(2026, 3, 2), 3600L)], game.Days.OrderBy(d => d.Date).Select(d => (d.Date, d.Seconds)));
        Assert.Equal([D(2026, 3, 1)], game.SessionDates);
        Assert.Equal(500, game.UndatedSeconds);
    }

    [Fact]
    public void DaylightSavingNeitherLosesNorInventsTime()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("W. Europe Standard Time");
        // 29 March 2026: clocks jump from 02:00 to 03:00 local.
        var game = PlayStatistics.FromHistory("Game", false, History(Session(new(2026, 3, 28, 22, 0, 0, TimeSpan.Zero), 6 * 3600)), zone, 0);
        Assert.Equal(6 * 3600, game.Days.Sum(d => d.Seconds));
        Assert.Equal(D(2026, 3, 28), Assert.Single(game.SessionDates));
    }

    [Fact]
    public void SteamAndTypedTimeCountOnlyInAllTime()
    {
        var today = D(2026, 11, 27);
        var zone = TimeZoneInfo.Utc;
        var game = PlayStatistics.FromHistory("Game", false, History(Session(new(2026, 11, 27, 10, 0, 0, TimeSpan.Zero), 100)), zone, 9000);
        var day = PlayStatistics.Summarize([game], PlayStatistics.Range(StatsPeriod.Today, today, DayOfWeek.Monday));
        Assert.Equal((100L, 1, 1, 1), (day.Seconds, day.Sessions, day.Games, day.DaysPlayed));
        var all = PlayStatistics.Summarize([game], null);
        Assert.Equal(100 + 500 + 9000, all.Seconds);
        Assert.Equal(9600, Assert.Single(all.Top).Seconds);
    }

    [Fact]
    public void WeeksStartOnTheDayWindowsSaysAndPeriodsEndToday()
    {
        var friday = D(2026, 11, 27);
        Assert.Equal((D(2026, 11, 23), friday), PlayStatistics.Range(StatsPeriod.Week, friday, DayOfWeek.Monday));
        Assert.Equal((D(2026, 11, 22), friday), PlayStatistics.Range(StatsPeriod.Week, friday, DayOfWeek.Sunday));
        Assert.Equal((D(2026, 11, 21), friday), PlayStatistics.Range(StatsPeriod.Week, friday, DayOfWeek.Saturday));
        Assert.Equal((D(2026, 11, 1), friday), PlayStatistics.Range(StatsPeriod.Month, friday, DayOfWeek.Monday));
        Assert.Equal((D(2026, 1, 1), friday), PlayStatistics.Range(StatsPeriod.Year, friday, DayOfWeek.Monday));
        Assert.Null(PlayStatistics.Range(StatsPeriod.AllTime, friday, DayOfWeek.Monday));
    }

    [Fact]
    public void TheMostPlayedGamesAreListedForThePeriodOnly()
    {
        var a = Game("A", 0, (D(2026, 10, 1), 900), (D(2026, 11, 2), 100));
        var b = Game("B", 0, (D(2026, 11, 3), 200));
        var idle = Game("Idle", 0, (D(2026, 9, 1), 50));
        var month = PlayStatistics.Summarize([a, b, idle], (D(2026, 11, 1), D(2026, 11, 30)));
        Assert.Equal(["B", "A"], month.Top.Select(g => g.Name));
        Assert.Equal((300L, 2, 2, 2), (month.Seconds, month.Sessions, month.Games, month.DaysPlayed));
    }

    [Fact]
    public void StreaksCountConsecutiveDaysAcrossGamesAndStayAliveUntilADayIsMissed()
    {
        var a = Game("A", 0, (D(2026, 11, 1), 10), (D(2026, 11, 2), 10), (D(2026, 11, 3), 10), (D(2026, 11, 25), 10));
        var b = Game("B", 0, (D(2026, 11, 4), 10), (D(2026, 11, 26), 10), (D(2026, 11, 20), 0));
        var today = D(2026, 11, 27);
        var streaks = PlayStatistics.Streaks([a, b], today);
        Assert.Equal(new PlayStreaks(2, 4, D(2026, 11, 1), D(2026, 11, 4)), streaks);          // yesterday still counts as running
        Assert.Equal(0, PlayStatistics.Streaks([a, b], D(2026, 11, 28)).Current);
        Assert.Equal(new PlayStreaks(0, 0, null, null), PlayStatistics.Streaks([], today));
    }

    [Fact]
    public void TheChartHasABarForEveryDayWeekOrMonthIncludingEmptyOnes()
    {
        var game = Game("A", 999, (D(2026, 11, 27), 60), (D(2026, 11, 20), 30), (D(2026, 9, 15), 10));
        var today = D(2026, 11, 27);
        var days = PlayStatistics.Chart([game], ActivityGrouping.Day, today, DayOfWeek.Monday, 8);
        Assert.Equal(8, days.Length);
        Assert.Equal((D(2026, 11, 20), 30L), (days[0].Start, days[0].Seconds));
        Assert.Equal(60, days[^1].Seconds);
        var weeks = PlayStatistics.Chart([game], ActivityGrouping.Week, today, DayOfWeek.Monday, 3);
        Assert.Equal([(D(2026, 11, 9), 0L), (D(2026, 11, 16), 30L), (D(2026, 11, 23), 60L)], weeks.Select(w => (w.Start, w.Seconds)));
        var months = PlayStatistics.Chart([game], ActivityGrouping.Month, today, DayOfWeek.Monday, 3);
        Assert.Equal([(D(2026, 9, 1), 10L), (D(2026, 10, 1), 0L), (D(2026, 11, 1), 90L)], months.Select(m => (m.Start, m.Seconds)));
    }

    [Fact]
    public void MonthsAndYearsWithPlayCanBeCompared()
    {
        var game = Game("A", 0, (D(2025, 12, 31), 100), (D(2026, 11, 1), 400));
        Assert.Equal([D(2026, 11, 1), D(2025, 12, 1)], PlayStatistics.Months([game], D(2026, 11, 27)));
        Assert.Equal([D(2026, 1, 1), D(2025, 1, 1)], PlayStatistics.Years([game], D(2026, 11, 27)));
        // The current month and year are always offered, even before anything was played in them.
        Assert.Equal([D(2027, 1, 1), D(2026, 11, 1), D(2025, 12, 1)], PlayStatistics.Months([game], D(2027, 1, 5)));
        var first = PlayStatistics.Summarize([game], PlayStatistics.MonthRange(D(2026, 11, 1)));
        var second = PlayStatistics.Summarize([game], PlayStatistics.MonthRange(D(2025, 12, 1)));
        Assert.Equal(300, first.Seconds - second.Seconds);
        Assert.Equal((D(2025, 1, 1), D(2025, 12, 31)), PlayStatistics.YearRange(D(2025, 1, 1)));
    }

    [Fact]
    public async Task TheServiceCountsRemovedGamesToo()
    {
        var root = Path.Combine(Path.GetTempPath(), "GameVault statistics", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "game"));
            var exe = Path.Combine(root, "game", "game.exe");
            File.WriteAllText(exe, "fixture only; never executed");
            var paths = ApplicationPaths.FromRoot(Path.Combine(root, "app"));
            Directory.CreateDirectory(paths.Root);
            var store = new AppDataStore(paths);
            var library = new LibraryService(paths, store);
            var id = (await library.SaveAsync(new(null, "Kept history", exe, Path.GetDirectoryName(exe)!, []))).Profile.Id;
            await store.SaveStatisticsAsync(new(1, id, [Session(new(2026, 11, 20, 10, 0, 0, TimeSpan.Zero), 300)], []));
            await library.SetArchivedAsync(id, true);
            var steam = new SteamHoursService(store, new SteamLocalData(null), new GameVault.Infrastructure.Launching.StoreGameLauncher(Path.Combine(root, "none")),
                (_, _) => Task.FromResult<SteamAppSummary?>(null));
            var data = await new StatisticsService(store, steam).LoadAsync(TimeZoneInfo.Utc);
            var game = Assert.Single(data.Games);
            Assert.True(game.Removed);
            Assert.Equal(300, game.Days.Sum(d => d.Seconds));
            Assert.False(data.HasSteamTime);
        }
        finally { try { Directory.Delete(root, true); } catch (IOException) { } }
    }
}
