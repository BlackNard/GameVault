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
