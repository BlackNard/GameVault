namespace GameVault.Core;

public enum StatsPeriod { Today, Week, Month, Year, AllTime }

/// <summary>One session: when it started (local time) and how long it counted.</summary>
public sealed record PlaySpan(DateTime Start, long Seconds);
/// <summary>One game's play: seconds per local day, the day each session started, and time that has no date (typed-in adjustments, Steam hours).</summary>
public sealed record GamePlay(Guid GameId, string Name, bool Removed, ActivityBucket[] Days, DateOnly[] SessionDates, long UndatedSeconds,
    PlaySpan[]? Spans = null, string[]? Genres = null, GameStatus Status = GameStatus.NotStarted, DateOnly? CompletedOn = null, int HalfStars = 0, GameStatusDetail? Detail = null);
/// <param name="NewGames">Games first played in the period.</param>
/// <param name="Completed">Names of games completed in the period, where the date is known.</param>
public sealed record SessionInsight(long AverageSeconds, long LongestSeconds, string? LongestGame, DateOnly? LongestOn, int NewGames, string[] Completed);
public sealed record PlayShare(string Name, long Seconds, double Fraction);
public sealed record YearReview(PeriodStats Stats, SessionInsight Insight, int LongestStreak, DateOnly? BusiestMonth, long BusiestSeconds, string? TopGenre);
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
        var spans = summary.Sessions.Where(s => s.AdjustedSeconds > 0)
            .Select(s => new PlaySpan(TimeZoneInfo.ConvertTime(s.Session.StartedAt, zone).DateTime, s.AdjustedSeconds)).ToArray();
        return new(data.Game.Id, name, removed, summary.Days, [.. spans.Select(s => DateOnly.FromDateTime(s.Start))],
            (summary.TotalSeconds ?? dated) - dated + steamSeconds, spans);
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

    private static bool Within(DateOnly date, (DateOnly From, DateOnly To)? range) => range is not { } r || date >= r.From && date <= r.To;

    /// <summary>Session figures for a range (null is all time). A session belongs to the day it started.</summary>
    public static SessionInsight Insight(IReadOnlyList<GamePlay> games, (DateOnly From, DateOnly To)? range)
    {
        var sessions = games.SelectMany(g => (g.Spans ?? []).Select(s => (g.Name, Span: s, Date: DateOnly.FromDateTime(s.Start))))
            .Where(s => Within(s.Date, range)).ToArray();
        var longest = sessions.OrderByDescending(s => s.Span.Seconds).ThenBy(s => s.Span.Start).FirstOrDefault();
        var started = games.Count(g => g.SessionDates.Length > 0 && Within(g.SessionDates.Min(), range));
        string[] completed = [.. games.Where(g => g.CompletedOn is { } on && Within(on, range)).OrderBy(g => g.CompletedOn).Select(g => g.Name)];
        return sessions.Length == 0 ? new(0, 0, null, null, started, completed)
            : new(sessions.Sum(s => s.Span.Seconds) / sessions.Length, longest.Span.Seconds, longest.Name, longest.Date, started, completed);
    }

    /// <summary>The player's average rating (in stars) of the rated games played in the range, or null.</summary>
    public static double? AverageStars(IReadOnlyList<GamePlay> games, (DateOnly From, DateOnly To)? range)
    {
        var played = Summarize(games, range).Top.Select(g => g.GameId).ToHashSet();
        var rated = games.Where(g => g.HalfStars > 0 && played.Contains(g.GameId)).ToArray();
        return rated.Length == 0 ? null : rated.Average(g => g.HalfStars) / 2.0;
    }

    /// <summary>Seconds per weekday, indexed by <see cref="DayOfWeek"/>.</summary>
    public static long[] Weekdays(IReadOnlyList<GamePlay> games, (DateOnly From, DateOnly To)? range)
    {
        var result = new long[7];
        foreach (var day in games.SelectMany(g => g.Days).Where(d => Within(d.Date, range))) result[(int)day.Date.DayOfWeek] += day.Seconds;
        return result;
    }

    /// <summary>Seconds per hour of the day: each session is spread over the hours from its start. Sessions that started in the range are counted.</summary>
    public static long[] Hours(IReadOnlyList<GamePlay> games, (DateOnly From, DateOnly To)? range)
    {
        var result = new long[24];
        foreach (var span in games.SelectMany(g => g.Spans ?? []).Where(s => Within(DateOnly.FromDateTime(s.Start), range)))
        {
            var at = span.Start;
            // A month is far longer than any real session; it only stops a damaged value from looping on.
            for (var left = Math.Min(span.Seconds, 31 * 24 * 3600L); left > 0;)
            {
                var step = Math.Min(left, Math.Max(1, 3600 - at.Minute * 60 - at.Second));
                result[at.Hour] += step;
                left -= step;
                if (at > DateTime.MaxValue.AddSeconds(-step)) break;
                at = at.AddSeconds(step);
            }
        }
        return result;
    }

    /// <summary>Every day of the last <paramref name="weeks"/> weeks, from the first day of the oldest week up to today.</summary>
    public static ActivityBucket[] Calendar(IReadOnlyList<GamePlay> games, DateOnly today, DayOfWeek weekStart, int weeks)
    {
        var first = WeekStart(today, weekStart).AddDays(-7 * (weeks - 1));
        var totals = games.SelectMany(g => g.Days).Where(d => d.Date >= first && d.Date <= today).GroupBy(d => d.Date).ToDictionary(g => g.Key, g => g.Sum(d => d.Seconds));
        return [.. Enumerable.Range(0, today.DayNumber - first.DayNumber + 1).Select(i => first.AddDays(i)).Select(d => new ActivityBucket(d, totals.GetValueOrDefault(d)))];
    }

    /// <summary>0 for nothing, then quarters of the busiest day.</summary>
    public static int Level(long seconds, long busiest) => seconds <= 0 || busiest <= 0 ? 0 : (int)Math.Clamp((seconds * 4 + busiest - 1) / busiest, 1, 4);

    private static PlayShare[] Shares(IEnumerable<(string Name, long Seconds)> parts)
    {
        var grouped = parts.GroupBy(p => p.Name).Select(g => (Name: g.Key, Seconds: g.Sum(p => p.Seconds))).Where(p => p.Seconds > 0)
            .OrderByDescending(p => p.Seconds).ThenBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
        var total = grouped.Sum(p => p.Seconds);
        return [.. grouped.Select(p => new PlayShare(p.Name, p.Seconds, (double)p.Seconds / total))];
    }

    /// <summary>The largest <paramref name="shown"/> games; the rest together as "Other games".</summary>
    public static PlayShare[] GameShares(IReadOnlyList<GamePlay> games, (DateOnly From, DateOnly To)? range, int shown)
    {
        var stats = Summarize(games, range);
        PlayShare Part(string name, long seconds) => new(name, seconds, (double)seconds / stats.Seconds);
        var result = stats.Top.Take(shown).Select(g => Part(g.Name, g.Seconds)).ToList();
        if (stats.Top.Length > shown) result.Add(Part("Other games", stats.Top.Skip(shown).Sum(g => g.Seconds)));
        return [.. result];
    }

    /// <summary>A game with several genres has its time divided equally between them, so the shares add up.</summary>
    public static PlayShare[] GenreShares(IReadOnlyList<GamePlay> games, (DateOnly From, DateOnly To)? range)
    {
        var times = Summarize(games, range).Top.ToDictionary(g => g.GameId, g => g.Seconds);
        return Shares(games.Where(g => times.ContainsKey(g.GameId)).SelectMany(g =>
        {
            string[] genres = g.Genres is { Length: > 0 } own ? own : ["Unknown genre"];
            var seconds = times[g.GameId];
            return genres.Select((genre, i) => (genre, seconds / genres.Length + (i == 0 ? seconds % genres.Length : 0)));
        }));
    }

    public static PlayShare[] StatusShares(IReadOnlyList<GamePlay> games, (DateOnly From, DateOnly To)? range)
    {
        var times = Summarize(games, range).Top.ToDictionary(g => g.GameId, g => g.Seconds);
        return Shares(games.Where(g => times.ContainsKey(g.GameId)).Select(g => (PlayStatuses.Label(PlayStatuses.From(g.Status, g.Detail)), times[g.GameId])));
    }

    public static YearReview Review(IReadOnlyList<GamePlay> games, int year)
    {
        var range = YearRange(new(year, 1, 1));
        var inYear = games.Select(g => g with { Days = [.. g.Days.Where(d => d.Date.Year == year)] }).ToArray();
        var busiest = inYear.SelectMany(g => g.Days).GroupBy(d => d.Date.Month).Select(g => (Month: g.Key, Seconds: g.Sum(d => d.Seconds)))
            .Where(m => m.Seconds > 0).OrderByDescending(m => m.Seconds).ThenBy(m => m.Month).FirstOrDefault();
        var genre = GenreShares(games, range).FirstOrDefault(s => s.Name != "Unknown genre")?.Name;
        // The whole history is used for "new games", so a game first played earlier is not new this year.
        return new(Summarize(games, range), Insight(games, range), Streaks(inYear, range.To).Longest,
            busiest.Seconds > 0 ? new DateOnly(year, busiest.Month, 1) : null, busiest.Seconds, genre);
    }

    /// <summary>Months that have play, newest first, always including the current one.</summary>
    public static DateOnly[] Months(IReadOnlyList<GamePlay> games, DateOnly today) => Periods(games, today, d => new(d.Year, d.Month, 1));
    public static DateOnly[] Years(IReadOnlyList<GamePlay> games, DateOnly today) => Periods(games, today, d => new(d.Year, 1, 1));

    private static DateOnly[] Periods(IReadOnlyList<GamePlay> games, DateOnly today, Func<DateOnly, DateOnly> start) =>
        [.. games.SelectMany(g => g.Days).Where(d => d.Seconds > 0).Select(d => start(d.Date)).Append(start(today)).Distinct().OrderDescending()];
}
