using GameVault.Core;
namespace GameVault.Infrastructure.Storage;

public sealed partial class AppDataStore
{
    public Task<StoredDocument<RestoreJournal>> LoadRestoreJournalAsync(Guid id)=>Run(()=>ReadRestore(id));
    private StoredDocument<RestoreJournal> ReadRestore(Guid id)=>documents.Read<RestoreJournal>(Path.Combine(Folder(id),"restore-state.json"),s=>ValidRestore(s,id));
    public Task RequireNoPendingRestoreAsync(Guid id)=>RequireRestoreAllowedAsync(id,null);
    public Task RequireRestoreAllowedAsync(Guid id,Guid? undoRecovery)=>Run(()=>RequireRestoreAllowed(ReadRestore(id),undoRecovery));
    private static void RequireRestoreAllowed(StoredDocument<RestoreJournal> doc,Guid? undoRecovery)
    {
        JsonDocuments.RequireWritable(doc);
        if(doc.Value is RestoreJournal state&&state.Phase is RestorePhase.Prepared or RestorePhase.RecoveryRequired&&(state.RequiredRecoveryId??state.OperationId)!=undoRecovery)
            throw new IOException("A previous restore needs recovery. Open Backups and undo recovery "+(state.RequiredRecoveryId??state.OperationId).ToString("N")+" before launching or modifying saves.");
    }
    public Task BeginRestoreAsync(RestoreJournal state,Guid? undoRecovery)=>Run(()=>
    {
        var game=LoadProfile(state.GameId);
        if(game.Profile.State!=DocumentState.Ready)throw new IOException("The game profile needs attention before restoring.");
        var previous=ReadRestore(state.GameId);RequireRestoreAllowed(previous,undoRecovery);
        var required=previous.Value is RestoreJournal pending&&pending.Phase is RestorePhase.Prepared or RestorePhase.RecoveryRequired?pending.RequiredRecoveryId??pending.OperationId:(Guid?)null;
        state=state with{RequiredRecoveryId=required};
        if(state.Phase!=RestorePhase.Prepared)throw new ArgumentException("Restore must be prepared before replacement.");
        documents.Write(Path.Combine(Folder(state.GameId),"restore-state.json"),state,s=>ValidRestore(s,state.GameId));
    });
    public Task FinishRestoreAsync(Guid id,Guid operationId,RestorePhase phase,string message)=>Run(()=>
    {
        var doc=ReadRestore(id);JsonDocuments.RequireWritable(doc);
        if(doc.Value is not RestoreJournal state||state.OperationId!=operationId||phase is not (RestorePhase.Completed or RestorePhase.RolledBack or RestorePhase.RecoveryRequired))throw new IOException("Restore journal changed; recovery copies were preserved.");
        if(phase==RestorePhase.RolledBack&&state.RequiredRecoveryId is not null)phase=RestorePhase.RecoveryRequired;
        documents.Write(Path.Combine(Folder(id),"restore-state.json"),state with{Phase=phase,Message=message},s=>ValidRestore(s,id));
    });
    private static bool ValidRestore(RestoreJournal s,Guid id)=>s.SchemaVersion==1&&s.GameId==id&&s.OperationId!=Guid.Empty&&s.RequiredRecoveryId!=Guid.Empty&&Enum.IsDefined(s.Phase)&&s.Phase!=RestorePhase.Failed&&s.CreatedAt!=default&&s.Message is {Length:>0 and <=8192};
}