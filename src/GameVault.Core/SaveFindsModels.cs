namespace GameVault.Core;

/// <summary>A folder that had files written while the game ran. <paramref name="Shared"/>: another game ran at the same time at least once.</summary>
public sealed record SaveFind(string Path, int Files, int Sessions, DateTimeOffset LastSeen, bool Shared);

/// <summary>Per-game <c>save-finds.json</c> (issue #111): what was seen while playing, and the folders the player said are not this game's saves.</summary>
public sealed record SaveFindsDocument(int SchemaVersion, Guid GameId, SaveFind[] Finds, string[] Rejected, long Revision = 1);
