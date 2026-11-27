using System.Collections.Concurrent;
using GameVault.Core;
using GameVault.Infrastructure.Storage;
namespace GameVault.Infrastructure.Saves;
public interface ISaveDiscoveryScanner { Task<SaveDiscoveryResult> ScanAsync(Guid gameId,CancellationToken token=default); }
public sealed record SaveDiscoveryChanged(Guid GameId,SaveDiscoveryResult Result);
/// <summary>A scan selected folders that were not selected before, or has new ones to ask about.</summary>
public sealed record SaveSelectionChanged(Guid GameId,string GameName,SaveSourceMapping[] NewlySelected,SaveSourceMapping[] NewlyToConfirm);
public sealed class SaveDiscoveryService(ApplicationPaths paths,AppDataStore store,SaveDiscoveryEnvironment environment,long largeFolderBytes=1L<<30):ISaveDiscoveryScanner
{
    // Discovery runs every few seconds during play: the game's identity and the name search are kept for a while.
    private static readonly TimeSpan SearchKept=TimeSpan.FromMinutes(2);
    private readonly ConcurrentDictionary<Guid,Search> searches=new();
    private sealed record Search(string Key,long At,GameSaveIdentity Game,IReadOnlyList<string> Named);
    /// <summary>Makes the next scan read the game's folder and search by name again.</summary>
    public void SearchAgain(Guid id)=>searches.TryRemove(id,out _);
    private Search Look(GameProfile profile,string fingerprint,CancellationToken token)
    {
        var key=fingerprint+"\n"+profile.Name;
        if(searches.TryGetValue(profile.Id,out var kept)&&kept.Key==key&&Environment.TickCount64-kept.At<SearchKept.TotalMilliseconds)return kept;
        var game=GameSaveIdentity.Read(profile);
        return searches[profile.Id]=new(key,Environment.TickCount64,game,SaveFinder.NamedFolders(game,environment,token));
    }
    private readonly ConcurrentDictionary<Guid,SemaphoreSlim> gates=new();
    public const string RejectedMessage=SaveSourceWords.Rejected;
    private const int MaxSelected=32;
    public event EventHandler<SaveDiscoveryChanged>? Changed;
    public event EventHandler<SaveSelectionChanged>? SelectionChanged;
    /// <summary>"Not this folder": remembered, unselected now and never picked or asked about again until restored.</summary>
    public async Task<SaveDiscoveryResult> RejectAsync(Guid id,Guid sourceId,CancellationToken token=default)
    {
        var stored=await store.LoadSaveDiscoveryAsync(id).ConfigureAwait(false);
        var source=stored.Value?.Sources.SingleOrDefault(s=>s.Id==sourceId)??throw new IOException("This save folder is no longer listed. Refresh and try again.");
        if(source.Origin==SaveSourceOrigin.Manual)throw new IOException("You chose this folder in Edit game; remove it there instead.");
        await store.UpdateSaveFindsAsync(id,d=>d with{
            Rejected=[..d.Rejected.Where(r=>!SaveSourcePolicy.Canonical(r).Equals(source.Path,StringComparison.OrdinalIgnoreCase)).TakeLast(AppDataStore.MaxSaveFinds-1),source.Path],
            Finds=d.Finds.Where(f=>!SaveSourcePolicy.Overlaps(f.Path,source.Path)).ToArray()}).ConfigureAwait(false);
        return await PerformAsync(id,null,null,true,token).ConfigureAwait(false);
    }
    /// <summary>The folder the player picked in a notice, kept like one typed in Edit game.</summary>
    public async Task<SaveDiscoveryResult> ChooseAsync(Guid id,string folder,CancellationToken token=default)
    {
        var game=await store.LoadProfileAsync(id).ConfigureAwait(false);
        if(game.Profile.State!=DocumentState.Ready||game.Profile.Value is not GameProfile profile)throw new IOException("The game profile needs attention.");
        var chosen=new SaveSourcePolicy(paths,[..environment.BroadRoots,Path.GetDirectoryName(profile.ExecutablePath)!,profile.WorkingDirectory])
            .ValidateSet([..profile.SaveSources,folder]);
        await store.SaveProfileAsync(profile with{SaveSources=chosen}).ConfigureAwait(false);
        return await PerformAsync(id,null,null,true,token).ConfigureAwait(false);
    }
    public async Task<SaveDiscoveryResult> RestoreAsync(Guid id,string path,CancellationToken token=default)
    {
        await store.UpdateSaveFindsAsync(id,d=>d with{Rejected=d.Rejected.Where(r=>!SaveSourcePolicy.Overlaps(r,path)).ToArray()}).ConfigureAwait(false);
        return await PerformAsync(id,null,null,true,token).ConfigureAwait(false);
    }
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
        if(result.Document.State==DocumentState.Ready&&selected.Length==0&&result.Phase is SaveDiscoveryPhase.WaitingForSave or SaveDiscoveryPhase.Unresolved or SaveDiscoveryPhase.Disabled)
            throw new SaveSourceMissingException();
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
            var finds=await store.LoadSaveFindsAsync(id).ConfigureAwait(false);
            pending=await Task.Run(()=>Build(profile,stored.Value,enabled,confirm,finds,token),token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();await store.SaveDiscoveryAsync(pending,stored.Value?.Revision??0).ConfigureAwait(false);
            var result=Summarize(new(pending,DocumentState.Ready));Publish(id,result);Announce(profile,stored.Value,pending);return result;
        }
        catch(Exception e)when(!strict&&e is IOException or UnauthorizedAccessException or ArgumentException or OverflowException)
        {
            var result=Error(stored.IsReadOnly?stored:new(pending??stored.Value,DocumentState.Unavailable,e.Message),"Discovery could not finish safely. "+e.Message);Publish(id,result);return result;
        }
        finally{gate.Release();}
    }
    private void Announce(GameProfile profile,SaveDiscoveryDocument? previous,SaveDiscoveryDocument next)
    {
        static HashSet<string> Paths(IEnumerable<SaveSourceMapping> mappings)=>mappings.Select(m=>m.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var had=Paths(previous?.Sources.Where(s=>s.Selected)??[]);var asked=Paths(previous?.Sources.Where(s=>s.CanConfirm)??[]);
        // The player's own choices need no announcement.
        var selected=next.Sources.Where(s=>s.Selected&&s.Origin==SaveSourceOrigin.KnownRule&&!had.Contains(s.Path)).ToArray();
        var confirm=next.Sources.Any(s=>s.Selected)?[]:next.Sources.Where(s=>s.CanConfirm&&!asked.Contains(s.Path)).ToArray();
        if(selected.Length>0||confirm.Length>0)SelectionChanged?.Invoke(this,new(profile.Id,profile.Name,selected,confirm));
    }
    private SaveDiscoveryDocument Build(GameProfile profile,SaveDiscoveryDocument? previous,bool enabled,Guid? confirm,StoredDocument<SaveFindsDocument> finds,CancellationToken token)
    {
        var identity=SaveDiscoveryIdentity.For(profile);var same=previous?.Fingerprint==identity;
        // Without the list of rejected folders nothing new may be selected.
        var rejectedKnown=finds.State is DocumentState.Missing or DocumentState.Ready||finds.Value is not null;
        var rejected=finds.Value?.Rejected??[];
        var policy=new SaveSourcePolicy(paths,[..environment.BroadRoots,Path.GetDirectoryName(profile.ExecutablePath)!,profile.WorkingDirectory]);
        var inspector=new SaveSourceInspector(policy);var warnings=new List<string>();var specs=new List<SaveCandidate>();var known=false;
        if(profile.SaveSources.Length>32)throw new IOException("Too many manual sources. Keep at most 32 narrow save locations.");
        if(profile.SaveSources.Length>0)specs.AddRange(profile.SaveSources.Select(p=>new SaveCandidate(p,SaveSourceOrigin.Manual,"Selected manually in Edit game")));
        GameSaveIdentity? game=null;
        if(profile.SaveSources.Length==0&&enabled)
        {
            var search=Look(profile,identity,token);game=search.Game;var catalog=SaveFinder.Candidates(profile,game,environment,finds.Value,search.Named,token);
            specs.AddRange(catalog.Candidates);warnings.AddRange(catalog.Warnings);known=catalog.Candidates.Any(c=>c.KnownLocation);
        }
        var mappings=new List<SaveSourceMapping>();var scores=new Dictionary<string,Scored>(StringComparer.OrdinalIgnoreCase);
        // One folder may be reached several ways: the first listing keeps its words and payload, the strongest way counts for its score.
        foreach(var group in specs.GroupBy(c=>SaveSourcePolicy.Canonical(c.Path),StringComparer.OrdinalIgnoreCase).Take(96))
        {
            token.ThrowIfCancellationRequested();var path=group.Key;var candidate=group.First();
            var old=previous?.Sources.FirstOrDefault(s=>s.Path.Equals(path,StringComparison.OrdinalIgnoreCase));
            // One pass over the folder answers both whether it is ready and what kind of files it holds.
            var contents=new Contents();
            var probe=inspector.Inspect(path,token,candidate.Origin==SaveSourceOrigin.Manual?null:p=>contents.Add(p,candidate.Payload));
            var approved=same&&old?.Origin==SaveSourceOrigin.Confirmed;
            var origin=approved?SaveSourceOrigin.Confirmed:candidate.Origin;
            // A folder selected before stays selected while its rule is waiting, or while it keeps holding saves.
            var kept=same&&old is{Selected:true,Origin:SaveSourceOrigin.KnownRule};
            var selected=candidate.Origin==SaveSourceOrigin.Manual||approved||(kept&&probe.Access!=SaveSourceAccess.Ready);
            var canConfirm=candidate.Origin!=SaveSourceOrigin.Manual&&probe.Access==SaveSourceAccess.Ready;
            if(canConfirm&&game is not null)
            {
                var scored=Score(path,group.ToArray(),contents,game,finds.Value,profile);scores[path]=scored with{Kept=kept};
                canConfirm=scored.Confidence!=SaveConfidence.Low||scored.IsRule;
            }
            mappings.Add(new(old?.Id??Guid.NewGuid(),path,probe.Kind,origin,selected,probe.Access,approved?"Confirmed by you · "+candidate.Evidence:candidate.Evidence,probe.Message,
                canConfirm,candidate.Payload));
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
        if(profile.SaveSources.Length==0&&enabled&&!mappings.Any(m=>m.Selected&&m.Origin==SaveSourceOrigin.Confirmed&&!Rejected(m.Path)))
            mappings=Choose(mappings,scores,rejected,rejectedKnown&&warnings.Count==0,warnings.Count>0);
        if(confirm is Guid sourceId)
        {
            var selected=mappings.SingleOrDefault(m=>m.Id==sourceId);
            if(!enabled||profile.SaveSources.Length>0||selected is null||!selected.CanConfirm||selected.Access!=SaveSourceAccess.Ready)throw new IOException("This source is no longer a current readable candidate. Refresh discovery or select a manual path through Edit game.");
            mappings=mappings.Select(m=>m.Id==sourceId?m with{Selected=true,Origin=SaveSourceOrigin.Confirmed,Evidence="Confirmed by you · "+m.Evidence}:m.Origin==SaveSourceOrigin.KnownRule?m with{Selected=false}:m).ToList();
        }
        mappings=mappings.Select(m=>m.Origin!=SaveSourceOrigin.Manual&&Rejected(m.Path)?
            m with{Selected=false,CanConfirm=false,Origin=SaveSourceOrigin.Suggestion,Message=RejectedMessage}:m).ToList();
        try{policy.ValidateSet(mappings.Where(m=>m.Selected&&m.Access!=SaveSourceAccess.Unsafe).Select(m=>m.Path));}
        catch(IOException e)
        {mappings=mappings.Select(m=>m.Selected?m with{Access=SaveSourceAccess.Unsafe,CanConfirm=false,Message=e.Message}:m).ToList();warnings.Add(e.Message);}
        // A folder holding nothing that could be a save (settings only) does not end the wait for the first save.
        if(known&&mappings.All(m=>m.Access is SaveSourceAccess.Missing or SaveSourceAccess.Empty||scores.TryGetValue(m.Path,out var s)&&s.Confidence==SaveConfidence.Low&&!s.IsRule))warnings.Add("Waiting for the game to create its first save in a known location; checks will retry during play.");
        var now=DateTimeOffset.UtcNow;if(previous is not null&&now<=previous.ScannedAt)now=previous.ScannedAt.AddTicks(1);
        return new(1,profile.Id,identity,enabled,now,mappings.ToArray(),warnings.Take(128).ToArray(),checked((previous?.Revision??0)+1));
        // Overlap counts both ways, so a rejected folder cannot come back as its parent or its child.
        bool Rejected(string path)=>rejected.Any(r=>SaveSourcePolicy.Overlaps(r,path));
    }
    private sealed record Scored(SaveConfidence Confidence,int Points,bool IsRule,string Evidence,bool Kept=false);
    /// <summary>What a folder's readable, nonempty files are, noted while it is inspected.</summary>
    private sealed class Contents
    {
        public bool Known,Other,Settings;public long Bytes;
        public bool Add(string file,string payload)
        {
            var matches=SaveCandidateRules.PayloadMatches(file,payload);
            if(SaveFileNames.IsSettings(file))Settings=true;
            else if(matches){Other=true;if(SaveFileNames.IsSave(file))Known=true;}
            try{Bytes+=new FileInfo(file).Length;}catch(Exception e)when(e is IOException or UnauthorizedAccessException){}
            return matches;
        }
    }
    private Scored Score(string path,SaveCandidate[] ways,Contents contents,GameSaveIdentity game,SaveFindsDocument? finds,GameProfile profile)
    {
        var isRule=ways.Any(w=>w.Source==SaveCandidateSource.KnownRule);
        // A rule, a store number and the database name one game's folder, so any file there that is not a settings file counts as a save.
        var specific=ways.Any(w=>w.Source is SaveCandidateSource.KnownRule or SaveCandidateSource.StoreNumber or SaveCandidateSource.Database);
        var saves=specific?contents.Other:contents.Known;
        var written=(finds?.Finds??[]).Where(f=>SaveSourcePolicy.Within(SaveSourcePolicy.Canonical(f.Path),path)).ToArray();
        // Only the part below the user folder or the installation says anything: what lies above is shared by every game there.
        var bases=environment.BroadRoots.Append(game.InstallRoot).Where(b=>!string.IsNullOrWhiteSpace(b)&&Path.IsPathFullyQualified(b)).Select(SaveSourcePolicy.Canonical).ToArray();
        var under=bases.Where(b=>path.Length>b.Length&&SaveSourcePolicy.Within(path,b)).MaxBy(b=>b.Length);
        var parts=(under is null?path[Path.GetPathRoot(path)!.Length..]:path[under.Length..]).Split(Path.DirectorySeparatorChar,StringSplitOptions.RemoveEmptyEntries);
        var named=parts.Any(part=>GameNameTokens.Matches(part,game.Tokens));
        var inInstall=SaveSourcePolicy.Within(path,SaveSourcePolicy.Canonical(Path.GetDirectoryName(profile.ExecutablePath)!));
        var bare=!specific&&under is not null&&!inInstall&&parts.Length==1;
        var best=ways.Select(w=>(Way:w,Facts:new SaveCandidateFacts(path,w.Source,named,saves,!saves&&contents.Settings,written.Length>0,written.Select(f=>f.Sessions).DefaultIfEmpty().Max(),
            written.Length>0&&written.All(f=>f.Shared)){Bare=bare,TooLarge=!specific&&contents.Bytes>largeFolderBytes}))
            .OrderByDescending(x=>SaveCandidateScore.Of(x.Facts)).ThenByDescending(x=>SaveCandidateScore.Points(x.Facts)).First();
        return new(SaveCandidateScore.Of(best.Facts),SaveCandidateScore.Points(best.Facts),isRule,best.Way.Evidence);
    }
    /// <summary>Which found folders are used without asking. Known rules keep their own care (exactly one, otherwise the player decides); without a
    /// ready rule every confident folder is used, the stronger or narrower one where two overlap.</summary>
    private static List<SaveSourceMapping> Choose(List<SaveSourceMapping> mappings,Dictionary<string,Scored> scores,string[] rejected,bool maySelect,bool ambiguous)
    {
        bool Allowed(SaveSourceMapping m)=>m.Access==SaveSourceAccess.Ready&&scores.ContainsKey(m.Path)&&!rejected.Any(r=>SaveSourcePolicy.Overlaps(r,m.Path));
        var rules=mappings.Where(m=>Allowed(m)&&scores[m.Path].IsRule&&m.Origin==SaveSourceOrigin.KnownRule).ToArray();
        // Selected before and no longer listed by this scan (the search is bounded): it keeps feeding backups.
        var chosen=ambiguous?[]:mappings.Where(m=>m.Selected&&m.Origin==SaveSourceOrigin.KnownRule&&!scores.ContainsKey(m.Path)&&m.Access==SaveSourceAccess.Ready&&
            !rejected.Any(r=>SaveSourcePolicy.Overlaps(r,m.Path))).ToList();
        var retained=chosen.Count;
        // A rule already feeding backups stays even while nothing new may be selected.
        if(rules.Length>0){if(rules.Length==1&&(maySelect||!ambiguous&&scores[rules[0].Path].Kept)&&!chosen.Any(c=>SaveSourcePolicy.Overlaps(c.Path,rules[0].Path)))chosen.Add(rules[0]);}
        else
        {
            // Folders that already feed backups come first and stay, whatever their score became.
            var usable=mappings.Where(m=>Allowed(m)&&(scores[m.Path].Kept||maySelect&&scores[m.Path].Confidence==SaveConfidence.High))
                .OrderByDescending(m=>scores[m.Path].Kept).ThenByDescending(m=>scores[m.Path].Points).ThenByDescending(m=>m.Path.Length);
            foreach(var m in usable)if(chosen.Count<MaxSelected&&!chosen.Any(c=>SaveSourcePolicy.Overlaps(c.Path,m.Path)))chosen.Add(m);
        }
        return mappings.Select(m=>
        {
            if(chosen.FindIndex(c=>c.Id==m.Id) is var at&&at>=0)return at<retained?m:m with{Selected=true,Origin=SaveSourceOrigin.KnownRule,
                Evidence=scores[m.Path].IsRule?m.Evidence:SaveSourceWords.FoundAutomatically+" · "+scores[m.Path].Evidence};
            if(ambiguous&&m.Origin==SaveSourceOrigin.KnownRule)return m with{Selected=false};
            var overlaps=chosen.Any(c=>SaveSourcePolicy.Overlaps(c.Path,m.Path));
            if(m.Access!=SaveSourceAccess.Ready)return overlaps?m with{Selected=false}:m;          // a folder waiting for its saves stays as it was
            return m with{Selected=false,CanConfirm=m.CanConfirm&&!overlaps};
        }).ToList();
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

/// <summary>No save folder is known for the game yet.</summary>
public sealed class SaveSourceMissingException() : IOException(SaveMissing.Message);

/// <summary>A readable save folder was found but the player has not confirmed it yet.</summary>
public sealed class SaveConfirmationRequiredException() : IOException(SaveConfirmation.Message);
