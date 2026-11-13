namespace GameVault.Core;

public enum RestoreAction { Write, Remove }
public enum RestorePhase { Prepared, Completed, RolledBack, RecoveryRequired, Failed }
public sealed record RestoreChoice(Guid? BackupId,Guid? RecoveryId);
public sealed record RestoreDestination(Guid SourceId,string Path);
public sealed record RestoreRequest(Guid GameId,RestoreChoice Choice,RestoreDestination[] Destinations,string[] ExtraRemovals);
public sealed record RestoreChange(Guid SourceId,string RelativePath,string TargetPath,RestoreAction Action,
    string? BeforeHash,long? BeforeSize,string? AfterHash,long? AfterSize);
public sealed record RestoreExtraFile(string Path,bool Remove);
public sealed record RestorePreview(Guid GameId,string SelectedLabel,RestoreDestination[] Destinations,RestoreChange[] Changes,RestoreExtraFile[] OtherFiles,string Fingerprint);
public sealed record RestoreResult(RestorePhase Phase,string Message,Guid? RecoveryId=null,string? RecoveryDirectory=null);
public sealed record RestoreJournal(int SchemaVersion,Guid GameId,Guid OperationId,RestorePhase Phase,DateTimeOffset CreatedAt,string Message,Guid? RequiredRecoveryId=null);
public sealed record RestoreRecoveryDocument(int SchemaVersion,Guid GameId,Guid Id,DateTimeOffset CreatedAt,RestoreChoice Choice,
    BackupSource[] Targets,BackupFile[] BeforeFiles,string[] BeforeDirectories,RestoreChange[] Changes,BackupFile[] SelectedFiles);
public sealed record RestoreRecoveryInfo(Guid Id,DateTimeOffset CreatedAt,string Directory,string Description);
public sealed record RestoreRecoveries(RestoreRecoveryInfo[] Snapshots,StoredDocument<RestoreJournal> Journal,string[] Warnings);