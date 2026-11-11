namespace GameVault.Core;

public sealed record GameDraft(Guid? Id, string Name, string ExecutablePath, string WorkingDirectory,
    string[] SaveSources, GameStatus Status = GameStatus.NotStarted);
public sealed record LibraryGame(Guid Id, GameProfile? Profile, bool IsReadOnly, bool ExecutableMissing,
    string? CoverFile, long? PlayedSeconds, string? Warning);
public sealed record LibrarySnapshot(LibraryGame[] Games, string[] Warnings);
public sealed record GameSaveResult(GameProfile Profile, string? Warning);

public sealed class DuplicateGameException(Guid existingId, bool archived)
    : InvalidOperationException(archived
        ? "This executable already has a removed profile. Cancel and restore it from Removed profiles to retain its history."
        : "This executable is already in your library. Edit the existing game instead.")
{
    public Guid ExistingId { get; } = existingId;
}
