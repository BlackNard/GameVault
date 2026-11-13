namespace GameVault.Core;

public enum BackupPhase { None, Working, Complete, Unchanged, Blocked, Failed }
public sealed record BackupSource(Guid Id,string Path,SaveSourceKind Kind);
public sealed record BackupFile(Guid SourceId,string RelativePath,string StoragePath,long Size,string Sha256);
public sealed record DeletedSaveFile(Guid SourceId,string RelativePath);
public sealed record BackupManifest(int SchemaVersion,Guid GameId,Guid Id,DateTimeOffset CreatedAt,
    string Fingerprint,BackupSource[] Sources,BackupFile[] Files,DeletedSaveFile[] Deleted);
public sealed record CompletedBackup(string Directory,BackupManifest Manifest);
public sealed record BackupResult(Guid GameId,BackupPhase Phase,string Message,CompletedBackup? LastSuccess=null);
public sealed record BackupStatus(int SchemaVersion,Guid GameId,BackupPhase Phase,string Message,DateTimeOffset UpdatedAt);