namespace GameVault.Core;

public sealed record BackupRetentionPolicy(int Limit,bool KeepEverything);
public sealed record BackupPins(int SchemaVersion,Guid GameId,long Revision,Guid[] PinnedIds);
public sealed record BackupHistoryEntry(CompletedBackup Backup,bool Pinned)
{
    public long SizeBytes=>Backup.Manifest.Files.Sum(f=>f.Size);
}
public sealed record BackupHistorySnapshot(Guid GameId,BackupHistoryEntry[] Entries,BackupRetentionPolicy? Policy,
    bool CanChange,string[] Warnings,int RecoveryFolders)
{
    public long TotalBytes=>Entries.Sum(e=>e.SizeBytes);
}
public sealed record RetentionResult(bool Complete,int Removed,string Message,BackupHistorySnapshot History);