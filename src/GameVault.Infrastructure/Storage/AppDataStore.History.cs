using GameVault.Core;
namespace GameVault.Infrastructure.Storage;
public sealed partial class AppDataStore
{
    private const string CorrectionPrefix = "GameVault session correction ";
    public Task<HistoryData> LoadHistoryAsync(Guid id, bool includeJournals = true) => Run(() =>
    {
        var game=LoadProfile(id); var warnings=new List<string>(); StoredSession[] journals=[]; TimeCorrection[] corrections=[];
        try { if(includeJournals) journals=LoadSessions(id); } catch(Exception e) when(e is IOException or UnauthorizedAccessException) { warnings.Add("Session journals cannot be read safely. "+e.Message); }
        try { corrections=ReadCorrections(id); } catch(Exception e) when(e is IOException or UnauthorizedAccessException) { warnings.Add(e.Message); }
        if(game.Profile.Message is string message) warnings.Add(message);
        warnings.AddRange(journals.Where(j=>j.Document.Message is not null).Select(j=>j.Document.Message!));
        return new HistoryData(game,journals,corrections,warnings.Distinct().ToArray());
    });
    public Task<TimeCorrection> CorrectSessionAsync(Guid gameId,Guid sessionId,long expectedSeconds,long newSeconds,string reason) => Run(() =>
    {
        if(expectedSeconds<0||newSeconds<0||string.IsNullOrWhiteSpace(reason)||reason.Length>2000)
            throw new ArgumentException("Enter a non-negative duration and a reason (up to 2,000 characters).");
        var game=LoadProfile(gameId); EnsureInactive(game); var records=ReadCorrections(gameId);
        var row=SessionHistory.Build(new(game,LoadSessions(gameId),records,[]),TimeZoneInfo.Utc).Sessions.SingleOrDefault(s=>s.Session.Id==sessionId);
        if(row is null||!row.CanCorrect) throw new IOException("Only a verified finished session can be corrected. Active, interrupted, unknown or protected sessions were kept unchanged.");
        if(row.AdjustedSeconds!=expectedSeconds) throw new IOException("The session changed since editing began. Refresh and review its current duration.");
        if(newSeconds==expectedSeconds) throw new ArgumentException("This is already the session's duration.");
        var now=DateTimeOffset.UtcNow; if(records.Length>0&&now<records[^1].CreatedAt) now=records[^1].CreatedAt;
        var c=new TimeCorrection(1,Guid.NewGuid(),gameId,sessionId,records.Length+1L,row.Session.DurationSeconds,expectedSeconds,newSeconds,reason.Trim(),now);
        var updated=ProjectCorrections(game.Statistics.Value!,[..records,c]);
        // Audit first. A failed statistics write is reconciled by correction ID before the next edit.
        documents.Write(Path.Combine(Folder(gameId),"Corrections",c.Id.ToString("N")+".json"),c,value=>ValidCorrection(value,gameId,c.Id));
        documents.Write(Path.Combine(Folder(gameId),"statistics.json"),updated,s=>StorageValidation.Statistics(s,gameId));
        return c;
    });
    public Task SetGameStatusAsync(Guid id,GameStatus status) => Run(() =>
    {
        if(!Enum.IsDefined(status)) throw new ArgumentException("Choose a supported game status.");
        var game=LoadProfile(id); EnsureInactive(game); var profile=game.Profile.Value! with { Status=status };
        documents.Write(Path.Combine(Folder(id),"profile.json"),profile,p=>StorageValidation.Profile(p,id));
        TrackCompletion(id,game.Profile.Value!.Status,status);
        ForgetStatusDetail(id,game.Profile.Value!.Status,status);
    });
    private void EnsureInactive(StoredGame game)
    {
        if(game.Profile.State!=DocumentState.Ready||game.Statistics.State!=DocumentState.Ready)
            throw new IOException("The game profile/statistics need attention before this change can be saved.");
        if(game.Statistics.Value!.Sessions.Any(s=>s.EndedAt is null)||LoadSessions(game.Id).Any(j=>j.Document.IsReadOnly||j.Document.Value?.State==SessionState.Active))
            throw new IOException("Finish the current tracked session or resolve its journal warning before correcting time or changing status.");
    }
    private TimeCorrection[] ReadCorrections(Guid id)
    {
        var folder=Path.Combine(Folder(id),"Corrections"); managed.Check(folder); if(!Directory.Exists(folder)) return [];
        var result=new List<TimeCorrection>();
        var files=Directory.GetFiles(folder).Where(f=>f.EndsWith(".json",StringComparison.OrdinalIgnoreCase)||f.EndsWith(".json.bak",StringComparison.OrdinalIgnoreCase))
            .Select(f=>f.EndsWith(".bak",StringComparison.OrdinalIgnoreCase)?f[..^4]:f).Distinct(StringComparer.OrdinalIgnoreCase);
        foreach(var file in files)
        {
            if(!Guid.TryParseExact(Path.GetFileNameWithoutExtension(file),"N",out var correctionId)||correctionId==Guid.Empty) throw new IOException("An unrecognized correction audit file needs attention.");
            var doc=documents.Read<TimeCorrection>(file,c=>ValidCorrection(c,id,correctionId));
            if(doc.State!=DocumentState.Ready||doc.Value is null) throw new IOException("Correction audit needs recovery. "+doc.Message);
            result.Add(doc.Value);
        }
        return result.OrderBy(c=>c.Sequence).ThenBy(c=>c.Id).ToArray();
    }
    private static bool ValidCorrection(TimeCorrection c,Guid gameId,Guid id)=>c.SchemaVersion==1&&c.Id==id&&c.GameId==gameId&&
        c.SessionId!=Guid.Empty&&c.Sequence>0&&c.MeasuredSeconds>=0&&c.PreviousSeconds>=0&&c.NewSeconds>=0&&
        !string.IsNullOrWhiteSpace(c.Reason)&&c.Reason.Length<=2000&&c.CreatedAt!=default;
    private static string CorrectionReason(TimeCorrection c)=>CorrectionPrefix+c.Id.ToString("N")+": "+c.Reason;
    private static GameStatistics ProjectCorrections(GameStatistics statistics,TimeCorrection[] records)
    {
        var adjusted=statistics.Sessions.ToDictionary(s=>s.Id,s=>s.DurationSeconds);
        var adjustments=statistics.Adjustments.ToDictionary(a=>a.Id); var ids=records.Select(c=>c.Id).ToHashSet();
        if(adjustments.Values.Any(a=>a.Reason.StartsWith(CorrectionPrefix,StringComparison.Ordinal)&&!ids.Contains(a.Id)))
            throw new IOException("A projected correction is missing its audit file. Restore that audit before changing statistics.");
        for(var i=0;i<records.Length;i++)
        {
            var c=records[i];var s=statistics.Sessions.SingleOrDefault(s=>s.Id==c.SessionId);
            if(c.Sequence!=i+1L||s is null||c.MeasuredSeconds!=s.DurationSeconds||adjusted[c.SessionId]!=c.PreviousSeconds||adjusted.ContainsKey(c.Id))
                throw new IOException("Correction audit conflicts with measured sessions or sequence. Existing data was preserved.");
            adjusted[c.SessionId]=c.NewSeconds;var projection=new PlaytimeAdjustment(c.Id,c.Delta,CorrectionReason(c));
            if(adjustments.TryGetValue(c.Id,out var old)&&old!=projection) throw new IOException("A correction conflicts with its statistics projection.");
            adjustments[c.Id]=projection;
        }
        _ = adjusted.Values.Aggregate(0L,(sum,value)=>checked(sum+value));
        var updated=statistics with { Adjustments=adjustments.Values.ToArray() };
        if(!StorageValidation.Statistics(updated,statistics.GameId)) throw new IOException("Correction totals overflow or fall below zero. Existing data was preserved.");
        return updated;
    }
    private StoredDocument<GameStatistics> ReconcileCorrections(Guid id,StoredDocument<GameProfile> profile,StoredDocument<GameStatistics> stats)
    {
        try
        {
            var records=ReadCorrections(id); if(stats.Value is null) return stats;
            var updated=ProjectCorrections(stats.Value,records); if(updated.Adjustments.SequenceEqual(stats.Value.Adjustments)) return stats;
            if(profile.State!=DocumentState.Ready||stats.State!=DocumentState.Ready) throw new IOException("Correction projection requires writable profile and statistics.");
            documents.Write(Path.Combine(Folder(id),"statistics.json"),updated,s=>StorageValidation.Statistics(s,id));
            return new(updated,DocumentState.Ready);
        }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException or ArgumentException or OverflowException)
        { return new(null,DocumentState.Unavailable,"Playtime corrections need attention; statistics are protected. "+e.Message); }
    }
    private void RequireCorrectionProjection(GameStatistics proposed,StoredGame current)
    {
        if(current.Statistics.State!=DocumentState.Ready) throw new IOException(current.Statistics.Message??"Statistics need recovery before writing.");
        var projected=ProjectCorrections(proposed,ReadCorrections(proposed.GameId));
        if(!projected.Adjustments.SequenceEqual(proposed.Adjustments)) throw new IOException("This write would discard a correction. Refresh statistics first.");
    }
}