namespace GameVault.Core;

public sealed record BackupDestinationSnapshot(string? DefaultRoot,string? GameRoot,string EffectiveDirectory,string[] HistoricalDirectories);
public sealed record BackupDestinationDocument(int SchemaVersion,string? DefaultRoot,Dictionary<Guid,string> GameRoots,string[] HistoricalRoots);
