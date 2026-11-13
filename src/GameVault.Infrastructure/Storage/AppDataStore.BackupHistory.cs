using GameVault.Core;
namespace GameVault.Infrastructure.Storage;

public sealed partial class AppDataStore
{
    public Task<StoredDocument<BackupPins>> LoadBackupPinsAsync(Guid id)=>Run(()=>ReadPins(id));
    private StoredDocument<BackupPins> ReadPins(Guid id)=>documents.Read<BackupPins>(Path.Combine(Folder(id),"backup-pins.json"),p=>ValidPins(p,id));
    public Task SaveBackupPinsAsync(BackupPins pins,long expectedRevision)=>Run(()=>
    {
        var game=LoadProfile(pins.GameId);
        if(game.Profile.State!=DocumentState.Ready)throw new IOException("The game profile needs attention before pins can change.");
        var previous=ReadPins(pins.GameId);JsonDocuments.RequireWritable(previous);
        if((previous.Value?.Revision??0)!=expectedRevision||pins.Revision!=checked(expectedRevision+1))throw new IOException("Backup pins changed. Refresh the history before trying again.");
        documents.Write(Path.Combine(Folder(pins.GameId),"backup-pins.json"),pins,p=>ValidPins(p,pins.GameId));
    });
    public Task SetBackupRetentionAsync(Guid id,BackupRetentionPolicy policy,BackupRetentionPolicy expected)=>Run(()=>
    {
        if(policy.Limit<=0)throw new ArgumentException("Keep at least one unpinned backup, or choose Keep everything.");
        var game=LoadProfile(id);
        if(game.Profile.State!=DocumentState.Ready||game.Profile.Value is not GameProfile profile)throw new IOException("The game profile needs attention before retention can change.");
        if(profile.BackupLimit!=expected.Limit||profile.KeepAllBackups!=expected.KeepEverything)throw new IOException("Retention settings changed. Refresh and review the current policy.");
        documents.Write(Path.Combine(Folder(id),"profile.json"),profile with{BackupLimit=policy.Limit,KeepAllBackups=policy.KeepEverything},p=>StorageValidation.Profile(p,id));
    });
    private static bool ValidPins(BackupPins p,Guid id)=>p.SchemaVersion==1&&p.GameId==id&&p.Revision>0&&p.PinnedIds is {Length:<=4096}&&p.PinnedIds.All(i=>i!=Guid.Empty)&&p.PinnedIds.Distinct().Count()==p.PinnedIds.Length;
}