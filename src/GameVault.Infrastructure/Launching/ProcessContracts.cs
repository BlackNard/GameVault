namespace GameVault.Infrastructure.Launching;

public readonly record struct ProcessIdentity(int Id, long CreatedUtcTicks);
public sealed record ProcessCandidate(ProcessIdentity Identity, int[] Ancestors);

public interface IGameProcess : IDisposable
{
    ProcessIdentity Identity { get; }
    bool HasExited { get; }
    bool Owns(ProcessCandidate candidate) => candidate.Identity.CreatedUtcTicks >= Identity.CreatedUtcTicks && candidate.Ancestors.Contains(Identity.Id);
    Task WaitForExitAsync(CancellationToken cancellationToken);
}

public interface IGameProcessSystem
{
    IGameProcess Start(string executable, string workingDirectory);
    ProcessCandidate[] Find(string executable);
    IGameProcess? Open(ProcessIdentity identity);
}
