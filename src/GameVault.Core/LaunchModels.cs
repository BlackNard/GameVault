namespace GameVault.Core;

public enum LaunchPhase { Stopped, Starting, Running, Failed }
public sealed record LaunchState(Guid GameId, LaunchPhase Phase, string Message, int? ProcessId = null)
{
    public bool IsActive => Phase is LaunchPhase.Starting or LaunchPhase.Running;
}
public sealed record LaunchSettings(int SchemaVersion, Guid GameId, string? TargetExecutablePath);

public sealed record GameSessionEnded(Guid GameId,bool ConfirmedExit);
public interface IGameSessionSignals
{
    event EventHandler<LaunchState>? StateChanged;
    event EventHandler<GameSessionEnded>? SessionEnded;
}