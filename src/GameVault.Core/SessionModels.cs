namespace GameVault.Core;

public enum SessionState { Active, Completed, Interrupted }
public sealed record SessionJournal(int SchemaVersion, Guid GameId, Guid SessionId,
    DateTimeOffset StartedAt, DateTimeOffset ObservedAt, long DurationTicks, SessionState State,
    int ProcessId, long ProcessCreatedUtcTicks, string ExecutablePath, string? ProcessGroupName, int RecoveryCount);
public sealed record StoredSession(Guid Id, StoredDocument<SessionJournal> Document);
public sealed record SessionProgress(Guid GameId, Guid SessionId, long SessionSeconds, long TotalSeconds, string? Warning, bool IsActive = true);
