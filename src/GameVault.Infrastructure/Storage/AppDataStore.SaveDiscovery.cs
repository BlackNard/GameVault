using GameVault.Core;
using GameVault.Infrastructure.Saves;
namespace GameVault.Infrastructure.Storage;
public sealed partial class AppDataStore
{
    public Task<StoredDocument<SaveDiscoveryDocument>> LoadSaveDiscoveryAsync(Guid gameId)=>Run(()=>
        documents.Read<SaveDiscoveryDocument>(Path.Combine(Folder(gameId),"save-discovery.json"),d=>ValidSaveDiscovery(d,gameId)));
    public Task SaveDiscoveryAsync(SaveDiscoveryDocument document,long? expectedRevision=null)=>Run(()=>
    {
        ArgumentNullException.ThrowIfNull(document);var game=LoadProfile(document.GameId);
        if(game.Profile.State!=DocumentState.Ready||game.Profile.Value is not GameProfile profile)throw new IOException("The game profile needs attention before save mappings can be updated.");
        if(document.Fingerprint!=SaveDiscoveryIdentity.For(profile))throw new IOException("The game's executable, working folder or manual save paths changed during discovery. Refresh before confirming sources.");
        if(!ValidSaveDiscovery(document,document.GameId))throw new ArgumentException("The discovery result contains invalid mappings.");
        if(expectedRevision is long revision)
        {
            var current=documents.Read<SaveDiscoveryDocument>(Path.Combine(Folder(document.GameId),"save-discovery.json"),d=>ValidSaveDiscovery(d,document.GameId));
            JsonDocuments.RequireWritable(current);
            if((current.Value?.Revision??0)!=revision)throw new IOException("Discovery settings changed during this check. Refresh and retry.");
        }
        new SaveSourcePolicy(paths,[Path.GetDirectoryName(profile.ExecutablePath)!,profile.WorkingDirectory]).ValidateSet(document.Sources.Where(s=>s.Selected&&s.Access!=SaveSourceAccess.Unsafe).Select(s=>s.Path));
        documents.Write(Path.Combine(Folder(document.GameId),"save-discovery.json"),document,d=>ValidSaveDiscovery(d,document.GameId));
    });
    private static bool ValidSaveDiscovery(SaveDiscoveryDocument d,Guid gameId)
    {
        if(d.SchemaVersion!=1||d.GameId!=gameId||d.Fingerprint is null||d.Fingerprint.Length!=64||!d.Fingerprint.All(Uri.IsHexDigit)||d.ScannedAt==default||d.Revision<1||
            d.Sources is null||d.Sources.Length>128||d.Warnings is null||d.Warnings.Length>128||d.Warnings.Any(w=>w is null||w.Length>8192))return false;
        try
        {
            if(d.Sources.Any(s=>s is null||s.Id==Guid.Empty||s.Path is null||!Path.IsPathFullyQualified(s.Path)||!Enum.IsDefined(s.Kind)||!Enum.IsDefined(s.Origin)||!Enum.IsDefined(s.Access)||
                s.Payload is not ("any" or "bin" or "save")||s.Evidence is null||s.Evidence.Length>2048||s.Message?.Length>8192||(s.Selected&&s.Origin==SaveSourceOrigin.Suggestion)))return false;
            return d.Sources.Select(s=>s.Id).Distinct().Count()==d.Sources.Length&&d.Sources.Select(s=>SaveSourcePolicy.Canonical(s.Path)).Distinct(StringComparer.OrdinalIgnoreCase).Count()==d.Sources.Length;
        }
        catch(ArgumentException){return false;}
    }
}