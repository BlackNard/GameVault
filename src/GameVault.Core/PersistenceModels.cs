namespace GameVault.Core;

public enum GameStatus { NotStarted, Playing, Completed, OnHold }
public enum DocumentState { Missing, Ready, Recovered, Corrupt, Unsupported, Unavailable }

public sealed record StoredDocument<T>(T? Value, DocumentState State, string? Message = null)
{
    public bool IsReadOnly => State is not (DocumentState.Ready or DocumentState.Missing);
}

public sealed record AppSettings(int SchemaVersion, string Theme, int DefaultBackupLimit,
    bool KeepAllBackups, bool ReduceMotion, bool CloseToTray = true, bool NotifyBackupFailures = true, bool WelcomeDismissed = false, bool ShowSplash = true)
{
    public static AppSettings Default => new(1, "Midnight", 20, false, false);
}

public sealed record GameProfile(int SchemaVersion, Guid Id, string Name, string ExecutablePath,
    string WorkingDirectory, string[] SaveSources, GameStatus Status, int BackupLimit,
    bool KeepAllBackups, bool IsArchived);

public sealed record PlaySession(Guid Id, DateTimeOffset StartedAt, DateTimeOffset? EndedAt, long DurationSeconds);
public sealed record PlaytimeAdjustment(Guid Id, long Seconds, string Reason);
public sealed record GameStatistics(int SchemaVersion, Guid GameId, PlaySession[] Sessions,
    PlaytimeAdjustment[] Adjustments)
{
    public static GameStatistics Empty(Guid gameId) => new(1, gameId, [], []);
}

public sealed record StoredGame(Guid Id, StoredDocument<GameProfile> Profile, StoredDocument<GameStatistics> Statistics);
public sealed record AppDataSnapshot(StoredDocument<AppSettings> Settings, StoredGame[] Games, string[] Warnings);
