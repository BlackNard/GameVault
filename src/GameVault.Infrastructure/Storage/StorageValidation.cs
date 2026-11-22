using GameVault.Core;

namespace GameVault.Infrastructure.Storage;

internal static class StorageValidation
{
    public static bool Settings(AppSettings value) => value.SchemaVersion == 1 &&
        ThemeCatalog.IsKnown(value.Theme) && value.DefaultBackupLimit > 0;

    public static bool Profile(GameProfile value, Guid expectedId) => value.SchemaVersion == 1 &&
        expectedId != Guid.Empty && value.Id == expectedId && !string.IsNullOrWhiteSpace(value.Name) &&
        Absolute(value.ExecutablePath) && Absolute(value.WorkingDirectory) &&
        value.SaveSources is not null && value.SaveSources.All(Absolute) &&
        Enum.IsDefined(value.Status) && value.BackupLimit > 0;

    public static bool Statistics(GameStatistics value, Guid expectedId)
    {
        if (value.SchemaVersion != 1 || expectedId == Guid.Empty || value.GameId != expectedId ||
            value.Sessions is null || value.Adjustments is null) return false;
        if (value.Sessions.Any(s => s is null || s.Id == Guid.Empty || s.DurationSeconds < 0 ||
            (s.EndedAt.HasValue && s.EndedAt.Value < s.StartedAt))) return false;
        if (value.Adjustments.Any(a => a is null || a.Id == Guid.Empty || string.IsNullOrWhiteSpace(a.Reason))) return false;
        var ids = value.Sessions.Select(s => s.Id).Concat(value.Adjustments.Select(a => a.Id)).ToArray();
        if (ids.Distinct().Count() != ids.Length) return false;
        try
        {
            var total = value.Sessions.Aggregate(0L, (n, s) => checked(n + s.DurationSeconds));
            return value.Adjustments.Aggregate(total, (n, a) => checked(n + a.Seconds)) >= 0;
        }
        catch (OverflowException) { return false; }
    }

    private static bool Absolute(string path) => !string.IsNullOrWhiteSpace(path) && Path.IsPathFullyQualified(path);

    public static bool Session(SessionJournal value, Guid gameId, Guid sessionId) => value.SchemaVersion == 1 &&
        gameId != Guid.Empty && sessionId != Guid.Empty && value.GameId == gameId && value.SessionId == sessionId &&
        value.ObservedAt >= value.StartedAt && value.DurationTicks >= 0 && Enum.IsDefined(value.State) &&
        value.ProcessId > 0 && value.ProcessCreatedUtcTicks > 0 && Absolute(value.ExecutablePath) && value.RecoveryCount >= 0 &&
        (value.ProcessGroupName is null || (value.ProcessGroupName.StartsWith("Local\\GameVault.Game.", StringComparison.Ordinal) &&
            Guid.TryParseExact(value.ProcessGroupName["Local\\GameVault.Game.".Length..], "N", out _)));
}
