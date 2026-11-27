namespace GameVault.Infrastructure.Launching;

public readonly record struct ProcessIdentity(int Id, long CreatedUtcTicks);
public sealed record ProcessCandidate(ProcessIdentity Identity, int[] Ancestors);

public interface IGameProcess : IDisposable
{
    ProcessIdentity Identity { get; }
    bool HasExited { get; }
    string? RecoveryGroupName => null;
    bool Owns(ProcessCandidate candidate) => candidate.Identity.CreatedUtcTicks >= Identity.CreatedUtcTicks && candidate.Ancestors.Contains(Identity.Id);
    Task WaitForExitAsync(CancellationToken cancellationToken);
}

/// <summary>A running program: who it is, which processes started it, and its file name.</summary>
public sealed record RunningProcess(ProcessCandidate Candidate, string Name);

/// <summary>What Stop needs beyond observing. A process system without it cannot close games.</summary>
public interface IGameProcessControl
{
    RunningProcess[] Running();
    /// <summary>Asks the process's windows to close, like clicking X. False when it has none. Access denied (5) means it runs as administrator.</summary>
    bool RequestClose(ProcessIdentity identity);
    /// <summary>Ends the process at once. A process that already ended is not an error.</summary>
    void End(ProcessIdentity identity);
    /// <summary>The same two things through Windows' permission prompt; error 1223 means it was declined.</summary>
    Task EndElevatedAsync(bool force, ProcessIdentity[] processes, CancellationToken token);
}

public interface IGameProcessSystem
{
    IGameProcess Start(string executable, string workingDirectory);
    /// <summary>Starts with launch arguments and optionally as administrator. Systems that cannot do either simply start the executable.</summary>
    IGameProcess Start(string executable, string workingDirectory, string arguments, bool elevated) => Start(executable, workingDirectory);
    ProcessCandidate[] Find(string executable);
    IGameProcess? Open(ProcessIdentity identity);
    IGameProcess? Recover(string executable, ProcessIdentity identity, string? groupName) =>
        Find(executable).Any(candidate => candidate.Identity == identity) ? Open(identity) : null;
}
