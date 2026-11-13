namespace GameVault.Core;
public enum ActivityGrouping { Day, Week, Month }
public enum SessionEndKind { Completed, Interrupted, Active, Unknown, Unavailable }
public sealed record TimeCorrection(int SchemaVersion, Guid Id, Guid GameId, Guid SessionId, long Sequence,
    long MeasuredSeconds, long PreviousSeconds, long NewSeconds, string Reason, DateTimeOffset CreatedAt)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public long Delta => checked(NewSeconds - PreviousSeconds);
}
public sealed record HistoryData(StoredGame Game, StoredSession[] Journals, TimeCorrection[] Corrections, string[] Warnings);
public sealed record HistorySession(PlaySession Session, long AdjustedSeconds, SessionEndKind EndKind,
    DateTimeOffset? ObservedAt, bool CanCorrect, bool Recovered);
public sealed record ActivityBucket(DateOnly Date, long Seconds);
public sealed record HistorySummary(HistorySession[] Sessions, long? TotalSeconds, long MeasuredSeconds,
    long UndatedAdjustments, long UnallocatedSessionSeconds, Guid? LongestSessionId, DateTimeOffset? LastPlayed,
    ActivityBucket[] Days, bool EstimatedSplits, string[] Warnings);