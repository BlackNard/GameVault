namespace GameVault.Core;

public enum StatsPeriod { Today, Week, Month, Year, AllTime }

/// <summary>One game's play: seconds per local day, the day each session started, and time that has no date (typed-in adjustments, Steam hours).</summary>
public sealed record GamePlay(Guid GameId, string Name, bool Removed, ActivityBucket[] Days, DateOnly[] SessionDates, long UndatedSeconds);
public sealed record GameTime(Guid GameId, string Name, long Seconds);
/// <param name="Top">Games played in the period, most time first.</param>
public sealed record PeriodStats(long Seconds, int Sessions, int Games, int DaysPlayed, GameTime[] Top);
/// <summary>Consecutive days with play, across all games.</summary>
public sealed record PlayStreaks(int Current, int Longest, DateOnly? LongestStart, DateOnly? LongestEnd);
public sealed record ChartBar(DateOnly Start, long Seconds);

public static class PlayStatistics
{
    /// <summary>Sessions are split at local midnight and use the player's time corrections.</summary>
    public static GamePlay FromHistory(string name, bool removed, HistoryData data, TimeZoneInfo zone, long steamSeconds)
    {
        var summary = SessionHistory.Build(data, zone);
        var dated = summary.Days.Sum(d => d.Seconds);
        var sessions = summary.Sessions.Where(s => s.AdjustedSeconds > 0)
            .Select(s => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(s.Session.StartedAt, zone).DateTime)).ToArray();
        return new(data.Game.Id, name, removed, summary.Days, sessions, (summary.TotalSeconds ?? dated) - dated + steamSeconds);
    }

    /// <summary>The first and last day of the running period; null means all time.</summary>
    public static (DateOnly From, DateOnly To)? Range(StatsPeriod period, DateOnly today, DayOfWeek weekStart) => period switch
    {
        StatsPeriod.Today => (today, today),
        StatsPeriod.Week => (WeekStart(today, weekStart), today),
        StatsPeriod.Month => (new(today.Year, today.Month, 1), today),
        StatsPeriod.Year => (new(today.Year, 1, 1), today),
        _ => null
    };

    public static DateOnly WeekStart(DateOnly date, DayOfWeek weekStart) => date.AddDays(-(((int)date.DayOfWeek - (int)weekStart + 7) % 7));
    public static (DateOnly From, DateOnly To) MonthRange(DateOnly month) => (new(month.Year, month.Month, 1), new(month.Year, month.Month, DateTime.DaysInMonth(month.Year, month.Month)));
    public static (DateOnly From, DateOnly To) YearRange(DateOnly year) => (new(year.Year, 1, 1), new(year.Year, 12, 31));

    /// <summary>Figures for a date range, or for all time (null), which is the only place undated time counts.</summary>
    public static PeriodStats Summarize(IReadOnlyList<GamePlay> games, (DateOnly From, DateOnly To)? range)
    {
        bool Inside(DateOnly date) => range is not { } r || date >= r.From && date <= r.To;
        var top = games.Select(g => new GameTime(g.GameId, g.Name,
                Math.Max(0, g.Days.Where(d => Inside(d.Date)).Sum(d => d.Seconds) + (range is null ? g.UndatedSeconds : 0))))
            .Where(g => g.Seconds > 0).OrderByDescending(g => g.Seconds).ThenBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
        var sessions = games.Sum(g => g.SessionDates.Count(Inside));
        var days = games.SelectMany(g => g.Days).Where(d => d.Seconds > 0 && Inside(d.Date)).Select(d => d.Date).Distinct().Count();
        return new(top.Sum(g => g.Seconds), sessions, top.Length, days, top);
    }

    /// <summary>The current streak is still running if the last day played is today or yesterday.</summary>
    public static PlayStreaks Streaks(IReadOnlyList<GamePlay> games, DateOnly today)
    {
        var days = games.SelectMany(g => g.Days).Where(d => d.Seconds > 0 && d.Date <= today).Select(d => d.Date).Distinct().Order().ToArray();
        int run = 0, longest = 0;
        DateOnly? start = null, end = null;
        for (var i = 0; i < days.Length; i++)
        {
            run = i > 0 && days[i].DayNumber == days[i - 1].DayNumber + 1 ? run + 1 : 1;
            if (run > longest) { longest = run; start = days[i - run + 1]; end = days[i]; }
        }
        var current = days.Length > 0 && today.DayNumber - days[^1].DayNumber <= 1 ? run : 0;
        return new(current, longest, start, end);
    }

    /// <summary>The last <paramref name="count"/> days, weeks or months up to today, oldest first; empty ones are kept.</summary>
    public static ChartBar[] Chart(IReadOnlyList<GamePlay> games, ActivityGrouping grouping, DateOnly today, DayOfWeek weekStart, int count)
    {
        DateOnly Bucket(DateOnly date) => grouping switch
        {
            ActivityGrouping.Week => WeekStart(date, weekStart),
            ActivityGrouping.Month => new(date.Year, date.Month, 1),
            _ => date
        };
        var totals = games.SelectMany(g => g.Days).Where(d => d.Date <= today).GroupBy(d => Bucket(d.Date)).ToDictionary(g => g.Key, g => g.Sum(d => d.Seconds));
        var last = Bucket(today);
        return [.. Enumerable.Range(0, count).Select(i => count - 1 - i).Select(back => grouping switch
        {
            ActivityGrouping.Week => last.AddDays(-7 * back),
            ActivityGrouping.Month => last.AddMonths(-back),
            _ => last.AddDays(-back)
        }).Select(start => new ChartBar(start, totals.GetValueOrDefault(start)))];
    }

    /// <summary>Months that have play, newest first, always including the current one.</summary>
    public static DateOnly[] Months(IReadOnlyList<GamePlay> games, DateOnly today) => Periods(games, today, d => new(d.Year, d.Month, 1));
    public static DateOnly[] Years(IReadOnlyList<GamePlay> games, DateOnly today) => Periods(games, today, d => new(d.Year, 1, 1));

    private static DateOnly[] Periods(IReadOnlyList<GamePlay> games, DateOnly today, Func<DateOnly, DateOnly> start) =>
        [.. games.SelectMany(g => g.Days).Where(d => d.Seconds > 0).Select(d => start(d.Date)).Append(start(today)).Distinct().OrderDescending()];
}
