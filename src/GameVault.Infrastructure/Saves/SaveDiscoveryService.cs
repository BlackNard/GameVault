using System.Collections.Concurrent;
using GameVault.Core;
using GameVault.Infrastructure.Storage;
namespace GameVault.Infrastructure.Saves;
public interface ISaveDiscoveryScanner { Task<SaveDiscoveryResult> ScanAsync(Guid gameId,CancellationToken token=default); }
public sealed record SaveDiscoveryChanged(Guid GameId,SaveDiscoveryResult Result);
public sealed class SaveDiscoveryService(ApplicationPaths paths,AppDataStore store,SaveDiscoveryEnvironment environment):ISaveDiscoveryScanner
{
    private readonly ConcurrentDictionary<Guid,SemaphoreSlim> gates=new();
    public event EventHandler<SaveDiscoveryChanged>? Changed;
    public Task<SaveDiscoveryResult> ScanAsync(Guid id,CancellationToken token=default)=>PerformAsync(id,null,null,false,token);
    public Task<SaveDiscoveryResult> ConfirmAsync(Guid id,Guid sourceId,CancellationToken token=default)=>PerformAsync(id,null,sourceId,true,token);
    public Task<SaveDiscoveryResult> SetAutomaticAsync(Guid id,bool enabled,CancellationToken token=default)=>PerformAsync(id,enabled,null,true,token);
    public async Task<SaveDiscoveryResult> ReadAsync(Guid id)
    {
        var game=await store.LoadProfileAsync(id).ConfigureAwait(false);var stored=await store.LoadSaveDiscoveryAsync(id).ConfigureAwait(false);
        if(stored.IsReadOnly)return Error(stored,stored.Message??"Discovery data needs recovery.");
        if(game.Profile.Value is not GameProfile profile||game.Profile.IsReadOnly)return Error(stored,"The game profile needs attention.");
        if(stored.Value is not null&&stored.Value.Fingerprint!=SaveDiscoveryIdentity.For(profile))return Error(stored,"Game paths changed. Refresh discovery before using any previous mappings.");
        return Summarize(stored);
    }
    public async Task<SaveSourceMapping[]> RequireVerifiedSourcesAsync(Guid id,CancellationToken token=default)
    {
        var result=await ScanAsync(id,token).ConfigureAwait(false);
        var selected=result.Document.Value?.Sources.Where(s=>s.Selected).ToArray()??[];
        if(result.Document.State!=DocumentState.Ready||result.Phase!=SaveDiscoveryPhase.SourcesFound||selected.Length==0||selected.Any(s=>s.Access!=SaveSourceAccess.Ready))
            throw result.Phase==SaveDiscoveryPhase.NeedsConfirmation?new SaveConfirmationRequiredException()
                :new IOException("Save sources are not fully verified/readable. "+result.Summary);
        return selected;
    }
    private async Task<SaveDiscoveryResult> PerformAsync(Guid id,bool? automatic,Guid? confirm,bool strict,CancellationToken token)
    {
        var gate=gates.GetOrAdd(id,_=>new(1,1));await gate.WaitAsync(token).ConfigureAwait(false);
        StoredDocument<SaveDiscoveryDocument> stored=new(null,DocumentState.Missing);SaveDiscoveryDocument? pending=null;
        try
        {
            token.ThrowIfCancellationRequested();var game=await store.LoadProfileAsync(id).ConfigureAwait(false);
            stored=await store.LoadSaveDiscoveryAsync(id).ConfigureAwait(false);JsonDocuments.RequireWritable(stored);
            if(game.Profile.State!=DocumentState.Ready||game.Profile.Value is not GameProfile profile)throw new IOException("The game profile needs attention; discovery cannot update it.");
            if(automatic is null&&confirm is null&&stored.Value is {Automatic:false} cached&&profile.SaveSources.Length==0&&cached.Fingerprint==SaveDiscoveryIdentity.For(profile))
            {var cachedResult=Summarize(stored);Publish(id,cachedResult);return cachedResult;}
            var enabled=automatic??stored.Value?.Automatic??true;
            pending=await Task.Run(()=>Build(profile,stored.Value,enabled,confirm,token),token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();await store.SaveDiscoveryAsync(pending,stored.Value?.Revision??0).ConfigureAwait(false);
            var result=Summarize(new(pending,DocumentState.Ready));Publish(id,result);return result;
        }
        catch(Exception e)when(!strict&&e is IOException or UnauthorizedAccessException or ArgumentException or OverflowException)
        {
            var result=Error(stored.IsReadOnly?stored:new(pending??stored.Value,DocumentState.Unavailable,e.Message),"Discovery could not finish safely. "+e.Message);Publish(id,result);return result;
        }
        finally{gate.Release();}
    }
    private SaveDiscoveryDocument Build(GameProfile profile,SaveDiscoveryDocument? previous,bool enabled,Guid? confirm,CancellationToken token)
    {
        var identity=SaveDiscoveryIdentity.For(profile);var same=previous?.Fingerprint==identity;
        var policy=new SaveSourcePolicy(paths,[..environment.BroadRoots,Path.GetDirectoryName(profile.ExecutablePath)!,profile.WorkingDirectory]);
        var inspector=new SaveSourceInspector(policy);var warnings=new List<string>();var specs=new List<SaveCandidate>();var known=false;
        if(profile.SaveSources.Length>32)throw new IOException("Too many manual sources. Keep at most 32 narrow save locations.");
        if(profile.SaveSources.Length>0)specs.AddRange(profile.SaveSources.Select(p=>new SaveCandidate(p,SaveSourceOrigin.Manual,"Selected manually in Edit game")));
        else if(enabled){var catalog=SaveCandidateRules.For(profile,environment);specs.AddRange(catalog.Candidates);warnings.AddRange(catalog.Warnings);known=catalog.Candidates.Any(c=>c.KnownLocation);}
        var mappings=new List<SaveSourceMapping>();
        foreach(var candidate in specs.DistinctBy(c=>SaveSourcePolicy.Canonical(c.Path),StringComparer.OrdinalIgnoreCase))
        {
            token.ThrowIfCancellationRequested();var path=SaveSourcePolicy.Canonical(candidate.Path);
            var old=previous?.Sources.FirstOrDefault(s=>s.Path.Equals(path,StringComparison.OrdinalIgnoreCase));
            var probe=inspector.Inspect(path,token,candidate.Origin==SaveSourceOrigin.Manual?null:p=>SaveCandidateRules.PayloadMatches(p,candidate.Payload));
            var approved=same&&old?.Origin==SaveSourceOrigin.Confirmed;
            var origin=approved?SaveSourceOrigin.Confirmed:candidate.Origin;
            var selected=candidate.Origin==SaveSourceOrigin.Manual||approved||(same&&old?.Selected==true&&old.Origin==SaveSourceOrigin.KnownRule&&probe.Access!=SaveSourceAccess.Ready);
            mappings.Add(new(old?.Id??Guid.NewGuid(),path,probe.Kind,origin,selected,probe.Access,approved?"Confirmed by you · "+candidate.Evidence:candidate.Evidence,probe.Message,
                candidate.Origin!=SaveSourceOrigin.Manual&&probe.Access==SaveSourceAccess.Ready,candidate.Payload));
        }
        if(previous is not null&&profile.SaveSources.Length==0)
        {
            foreach(var old in previous.Sources.Where(o=>o.Origin!=SaveSourceOrigin.Manual&&!mappings.Any(m=>m.Path.Equals(o.Path,StringComparison.OrdinalIgnoreCase))).Take(128-mappings.Count))
            {
                token.ThrowIfCancellationRequested();
                if(!enabled||!same)
                {
                    mappings.Add(old with{Selected=false,CanConfirm=false,Access=SaveSourceAccess.NotChecked,Message=!enabled?"Not checked while automatic discovery is disabled":"Previous game identity; not checked or selected",Origin=same?old.Origin:SaveSourceOrigin.Suggestion});
                    continue;
                }
                var probe=inspector.Inspect(old.Path,token,p=>SaveCandidateRules.PayloadMatches(p,old.Payload));
                var retained=same&&enabled&&old.Selected;
                mappings.Add(old with{Kind=probe.Kind,Access=probe.Access,Selected=retained,Origin=retained?old.Origin:SaveSourceOrigin.Suggestion,CanConfirm=false,
                    Evidence=retained?old.Evidence:"Previous mapping; use Edit game to explicitly select it again",Message=probe.Message});
            }
        }
        if(!enabled&&profile.SaveSources.Length==0)mappings=mappings.Select(m=>m with{Selected=false,CanConfirm=false}).ToList();
        if(profile.SaveSources.Length==0&&enabled&&!mappings.Any(m=>m.Selected&&m.Origin==SaveSourceOrigin.Confirmed))
        {
            var ready=mappings.Where(m=>m.Origin==SaveSourceOrigin.KnownRule&&m.Access==SaveSourceAccess.Ready&&specs.Any(c=>c.Path.Equals(m.Path,StringComparison.OrdinalIgnoreCase))).ToArray();
            if(ready.Length==1&&warnings.Count==0)mappings=mappings.Select(m=>m.Id==ready[0].Id?m with{Selected=true}:m).ToList();
            else if(ready.Length>1||warnings.Count>0)mappings=mappings.Select(m=>m.Origin==SaveSourceOrigin.KnownRule?m with{Selected=false}:m).ToList();
        }
        if(confirm is Guid sourceId)
        {
            var selected=mappings.SingleOrDefault(m=>m.Id==sourceId);
            if(!enabled||profile.SaveSources.Length>0||selected is null||!selected.CanConfirm||selected.Access!=SaveSourceAccess.Ready)throw new IOException("This source is no longer a current readable candidate. Refresh discovery or select a manual path through Edit game.");
            mappings=mappings.Select(m=>m.Id==sourceId?m with{Selected=true,Origin=SaveSourceOrigin.Confirmed,Evidence="Confirmed by you · "+m.Evidence}:m.Origin==SaveSourceOrigin.KnownRule?m with{Selected=false}:m).ToList();
        }
        try{policy.ValidateSet(mappings.Where(m=>m.Selected&&m.Access!=SaveSourceAccess.Unsafe).Select(m=>m.Path));}
        catch(IOException e)
        {mappings=mappings.Select(m=>m.Selected?m with{Access=SaveSourceAccess.Unsafe,CanConfirm=false,Message=e.Message}:m).ToList();warnings.Add(e.Message);}
        if(known&&mappings.All(m=>m.Access is SaveSourceAccess.Missing or SaveSourceAccess.Empty))warnings.Add("Waiting for the game to create its first save in a known location; checks will retry during play.");
        var now=DateTimeOffset.UtcNow;if(previous is not null&&now<=previous.ScannedAt)now=previous.ScannedAt.AddTicks(1);
        return new(1,profile.Id,identity,enabled,now,mappings.ToArray(),warnings.Take(128).ToArray(),checked((previous?.Revision??0)+1));
    }
    private static SaveDiscoveryResult Summarize(StoredDocument<SaveDiscoveryDocument> stored)
    {
        var doc=stored.Value;if(doc is null)return new(stored,SaveDiscoveryPhase.WaitingForSave,"Discovery has not run yet. Launching needs no pre-existing save folder.");
        var selected=doc.Sources.Where(s=>s.Selected).ToArray();
        if(selected.Any(s=>s.Access is SaveSourceAccess.Unsafe or SaveSourceAccess.Unreadable or SaveSourceAccess.Limited))return Error(stored,"A selected source is unsafe, unreadable or exceeded inspection limits. Review the source details.");
        if(selected.Any(s=>s.Access is SaveSourceAccess.Missing or SaveSourceAccess.Empty))return new(stored,SaveDiscoveryPhase.WaitingForSave,"Waiting for selected saves to exist and be readable. Discovery will retry. Existing backups are kept.");
        if(selected.Length>0)return new(stored,SaveDiscoveryPhase.SourcesFound,$"{selected.Length} readable source(s) found. Backup status is shown separately in your library.");
        if(!doc.Automatic)return new(stored,SaveDiscoveryPhase.Disabled,"Automatic discovery is disabled. Choose manual sources in Edit game or enable discovery.");
        if(doc.Sources.Any(s=>s.CanConfirm))return new(stored,SaveDiscoveryPhase.NeedsConfirmation,SaveConfirmation.Message);
        if(doc.Warnings.Any(w=>w.StartsWith("Waiting for the game",StringComparison.Ordinal)))return new(stored,SaveDiscoveryPhase.WaitingForSave,"Waiting for the first save in a known location. Discovery will retry during play and after exit.");
        return new(stored,SaveDiscoveryPhase.Unresolved,"No verified source yet. Discovery will retry during play; use Edit game for a manual override if needed.");
    }
    private static SaveDiscoveryResult Error(StoredDocument<SaveDiscoveryDocument> stored,string message)=>new(stored,SaveDiscoveryPhase.Error,message);
    private void Publish(Guid id,SaveDiscoveryResult result)=>Changed?.Invoke(this,new(id,result));
}

/// <summary>A readable save folder was found but the player has not confirmed it yet.</summary>
public sealed class SaveConfirmationRequiredException() : IOException(SaveConfirmation.Message);
