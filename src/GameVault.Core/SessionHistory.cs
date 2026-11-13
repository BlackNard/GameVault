using System.Numerics;
namespace GameVault.Core;
public static class SessionHistory
{
    public static HistorySummary Build(HistoryData data, TimeZoneInfo zone)
    {
        var warnings = new List<string>(data.Warnings);
        if (data.Game.Statistics.Message is string message) warnings.Add(message);
        var statistics = data.Game.Statistics.Value;
        if (statistics is null) return new([], null, 0, 0, 0, null, null, [], false, warnings.ToArray());
        var linked = data.Corrections.GroupBy(c => c.SessionId).ToDictionary(g => g.Key, g => g.Aggregate(0L, (sum, c) => checked(sum + c.Delta)));
        var rows = statistics.Sessions.Select(session =>
        {
            var stored = data.Journals.SingleOrDefault(j => j.Id == session.Id)?.Document;
            var journal = stored?.Value;
            var consistent = journal is not null && journal.StartedAt == session.StartedAt && journal.DurationTicks / TimeSpan.TicksPerSecond == session.DurationSeconds &&
                (journal.State == SessionState.Active ? session.EndedAt is null : journal.ObservedAt == session.EndedAt);
            var kind = stored?.State is null or DocumentState.Missing ? session.EndedAt is null ? SessionEndKind.Active : SessionEndKind.Unknown :
                stored.State != DocumentState.Ready || !consistent ? SessionEndKind.Unavailable : journal!.State switch
                { SessionState.Active => SessionEndKind.Active, SessionState.Interrupted => SessionEndKind.Interrupted, _ => SessionEndKind.Completed };
            return new HistorySession(session, checked(session.DurationSeconds + linked.GetValueOrDefault(session.Id)), kind,
                consistent ? journal!.ObservedAt : session.EndedAt,
                kind == SessionEndKind.Completed && data.Game.Profile.State == DocumentState.Ready && data.Game.Statistics.State == DocumentState.Ready,
                journal?.RecoveryCount > 0);
        }).OrderByDescending(s => s.Session.StartedAt).ThenBy(s => s.Session.Id).ToArray();
        if (rows.Any(s => s.EndKind == SessionEndKind.Active) || data.Journals.Any(j => j.Document.IsReadOnly))
            rows = rows.Select(s => s with { CanCorrect = false }).ToArray();
        var days = new Dictionary<DateOnly,long>(); long unallocated = 0; bool estimates = false;
        foreach (var row in rows)
        {
            var end = row.EndKind == SessionEndKind.Unavailable ? null : row.Session.EndedAt ?? row.ObservedAt;
            var slices = AllocateDays(row.Session with { EndedAt = end }, row.AdjustedSeconds, zone);
            if (slices.Length == 0) unallocated = checked(unallocated + row.AdjustedSeconds);
            if (slices.Length > 1) estimates = true;
            foreach (var slice in slices) days[slice.Date] = checked(days.GetValueOrDefault(slice.Date) + slice.Seconds);
        }
        if (unallocated > 0) warnings.Add("Some recorded time has no trustworthy reporting interval; it remains in the all-time total and is shown as unallocated.");
        var correctionIds = data.Corrections.Select(c => c.Id).ToHashSet();
        var legacy = statistics.Adjustments.Where(a => !correctionIds.Contains(a.Id)).Aggregate(0L, (sum, a) => checked(sum + a.Seconds));
        var measured = rows.Aggregate(0L, (sum, row) => checked(sum + row.Session.DurationSeconds));
        var total = statistics.Adjustments.Aggregate(measured, (sum, a) => checked(sum + a.Seconds));
        var longest = rows.Where(r => r.EndKind != SessionEndKind.Active && r.EndKind != SessionEndKind.Unavailable)
            .OrderByDescending(r => r.AdjustedSeconds).ThenBy(r => r.Session.Id).FirstOrDefault()?.Session.Id;
        return new(rows, total, measured, legacy, unallocated, longest, rows.FirstOrDefault()?.Session.StartedAt,
            days.OrderByDescending(p => p.Key).Select(p => new ActivityBucket(p.Key,p.Value)).ToArray(), estimates, warnings.Distinct().ToArray());
    }

    public static ActivityBucket[] AllocateDays(PlaySession session, long seconds, TimeZoneInfo zone)
    {
        if (seconds < 0) throw new ArgumentException("A session duration cannot be negative.");
        if (session.EndedAt is not DateTimeOffset end || end < session.StartedAt || end - session.StartedAt > TimeSpan.FromDays(36600)) return [];
        var start = session.StartedAt;
        var firstDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(start,zone).DateTime);
        if (end == start) return [new(firstDate, seconds)];
        var result = new List<ActivityBucket>(); var cursor = start; long allocated = 0;
        try
        {
            while (cursor < end)
            {
                var date = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(cursor,zone).DateTime);
                var next = BoundaryUtc(date.AddDays(1), zone);
                if (next <= cursor) return [];
                var stop = next < end ? next : end;
                var cumulative = (long)((BigInteger)seconds * (stop.UtcTicks - start.UtcTicks) / (end.UtcTicks - start.UtcTicks));
                result.Add(new(date, checked(cumulative - allocated))); allocated = cumulative; cursor = stop;
            }
        }
        catch (ArgumentException) { return []; }
        return result.ToArray();
    }
    private static DateTimeOffset BoundaryUtc(DateOnly date, TimeZoneInfo zone)
    {
        var local = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        for (var minutes = 0; zone.IsInvalidTime(local); minutes++)
        {
            if (minutes >= 2880) throw new ArgumentException("Timezone boundary unavailable.");
            local = local.AddMinutes(1);
        }
        if (zone.IsAmbiguousTime(local)) return new DateTimeOffset(local,zone.GetAmbiguousTimeOffsets(local).Max()).ToUniversalTime();
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local,zone));
    }
    public static DateOnly PeriodStart(DateOnly date, ActivityGrouping grouping) => grouping switch
    {
        ActivityGrouping.Week => date.AddDays(-(((int)date.DayOfWeek + 6) % 7)),
        ActivityGrouping.Month => new(date.Year,date.Month,1), _ => date
    };
}