namespace GameVault.Core;

public enum LaunchPhase { Stopped, Starting, Running, Failed }
public sealed record LaunchState(Guid GameId, LaunchPhase Phase, string Message, int? ProcessId = null)
{
    public bool IsActive => Phase is LaunchPhase.Starting or LaunchPhase.Running;
}
/// <summary>One way to start a game (for example another version, a mod manager or a launcher). Arguments are passed as plain text, never through a shell.</summary>
public sealed record PlayAction(Guid Id, string Name, string ExecutablePath, string Arguments, string WorkingDirectory, bool RunAsAdministrator);

/// <summary>A command the owner configured to run before the game starts or after it exits.</summary>
public sealed record LaunchHook(string ExecutablePath, string Arguments, int TimeoutSeconds, bool WaitForExit);

public sealed record LaunchSettings(int SchemaVersion, Guid GameId, string? TargetExecutablePath, PlayAction[]? PlayActions = null, Guid? DefaultActionId = null,
    LaunchHook? BeforeLaunch = null, LaunchHook? AfterExit = null);

public static class LaunchRules
{
    public const int MaxActions = 8;
    public const int MaxNameLength = 40;
    public const int MaxArgumentLength = 2000;
    public const int MaxTimeoutSeconds = 300;

    private static bool IsExe(string? path) => !string.IsNullOrWhiteSpace(path) && Path.IsPathFullyQualified(path) &&
        string.Equals(Path.GetExtension(path), ".exe", StringComparison.OrdinalIgnoreCase);

    private static bool GoodArguments(string? arguments) => arguments is not null && arguments.Length <= MaxArgumentLength && !arguments.Any(char.IsControl);

    public static bool IsValidAction(PlayAction? action) => action is not null && action.Id != Guid.Empty && !string.IsNullOrWhiteSpace(action.Name) && action.Name.Trim().Length <= MaxNameLength &&
        IsExe(action.ExecutablePath) && GoodArguments(action.Arguments) && (string.IsNullOrEmpty(action.WorkingDirectory) || Path.IsPathFullyQualified(action.WorkingDirectory));

    public static bool IsValidHook(LaunchHook? hook) => hook is null || (IsExe(hook.ExecutablePath) && GoodArguments(hook.Arguments) && hook.TimeoutSeconds is >= 1 and <= MaxTimeoutSeconds);

    public static bool IsValid(LaunchSettings value, Guid gameId)
    {
        if (value.SchemaVersion != 1 || value.GameId != gameId) return false;
        if (value.TargetExecutablePath is not null && !IsExe(value.TargetExecutablePath)) return false;
        var actions = value.PlayActions ?? [];
        if (actions.Length > MaxActions || !actions.All(IsValidAction)) return false;
        if (actions.Select(a => a.Id).Distinct().Count() != actions.Length || actions.Select(a => a.Name.Trim().ToUpperInvariant()).Distinct().Count() != actions.Length) return false;
        if (value.DefaultActionId is Guid defaultId && actions.All(a => a.Id != defaultId)) return false;
        return IsValidHook(value.BeforeLaunch) && IsValidHook(value.AfterExit);
    }

    /// <summary>The action the Play button runs: the chosen default, or none (the game's own executable).</summary>
    public static PlayAction? DefaultAction(LaunchSettings? settings) => settings?.PlayActions?.FirstOrDefault(a => a.Id == settings.DefaultActionId);

    public static PlayAction? Find(LaunchSettings? settings, Guid? actionId) => actionId is Guid id ? settings?.PlayActions?.FirstOrDefault(a => a.Id == id) : null;
}

public sealed record GameSessionEnded(Guid GameId,bool ConfirmedExit);

public enum StopOutcome { Closed, StillRunning, Cancelled, NotRunning, Failed }
/// <summary>What Stop did; <paramref name="Message"/> is for the player.</summary>
public sealed record StopResult(StopOutcome Outcome, string Message);

/// <summary>Stop closes a game and what it started, never other programs a game may have opened.</summary>
public static class StopRules
{
    private static readonly string[] Spared =
    [
        "steam", "steamwebhelper", "steamservice", "epicgameslauncher", "epicwebhelper", "galaxyclient", "galaxyclient helper", "galaxyclientservice",
        "galaxycommunication", "ubisoftconnect", "upc", "eadesktop", "eabackgroundservice", "origin", "battle.net",
        "explorer", "chrome", "msedge", "firefox", "brave", "opera", "discord", "gamevault"
    ];

    public static bool IsSpared(string programName) => Spared.Contains(Path.GetFileNameWithoutExtension(programName), StringComparer.OrdinalIgnoreCase);
}
public interface IGameSessionSignals
{
    event EventHandler<LaunchState>? StateChanged;
    event EventHandler<GameSessionEnded>? SessionEnded;
}