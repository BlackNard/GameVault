namespace GameVault.Core;

public sealed record GameDraft(Guid? Id, string Name, string ExecutablePath, string WorkingDirectory,
    string[] SaveSources, GameStatus Status = GameStatus.NotStarted, bool RemoveCover = false, bool BesideRemoved = false, PlayStatus? Detailed = null);
public sealed record LibraryGame(Guid Id, GameProfile? Profile, bool IsReadOnly, bool ExecutableMissing,
    string? CoverFile, long? PlayedSeconds, string? Warning, DateTimeOffset? LastPlayedAt = null, string? HeroFile = null, int Rating = 0, GameStatusDetail? Detail = null);
public sealed record LibrarySnapshot(LibraryGame[] Games, string[] Warnings);
public sealed record GameSaveResult(GameProfile Profile, string? Warning);

public sealed class DuplicateGameException(Guid existingId, bool archived)
    : InvalidOperationException(archived
        ? "This executable has a removed profile. Restore its saved configuration and history to use it again."
        : "This executable is already in your library. Edit the existing game instead.")
{
    public Guid ExistingId { get; } = existingId;
    public bool IsArchived { get; } = archived;
}
