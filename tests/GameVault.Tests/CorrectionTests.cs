using GameVault.Core;
using GameVault.Infrastructure.Storage;
namespace GameVault.Tests;
public sealed class CorrectionTests : IDisposable
{
    private readonly string root=Path.Combine(Path.GetTempPath(),"GameVault corrections",Guid.NewGuid().ToString("N"));
    private readonly Guid id=Guid.NewGuid(), sessionId=Guid.NewGuid();
    private AppDataStore Store => new(ApplicationPaths.FromRoot(root));
    private string Folder => Path.Combine(root,"Profiles",id.ToString("N"));
    private async Task Seed()
    {
        Directory.CreateDirectory(root);await Store.InitializeAsync();
        await Store.SaveProfileAsync(new(1,id,"Game",@"D:\Game\game.exe",@"D:\Game",[],GameStatus.Playing,20,false,false));
        var start=DateTimeOffset.Parse("2026-11-12T10:00:00Z");
        await Store.SaveSessionJournalAsync(new(1,id,sessionId,start,start.AddHours(1),3600*TimeSpan.TicksPerSecond,SessionState.Completed,12,123,@"D:\Game\game.exe",null,0));
        await Store.SaveNoteAsync(id,sessionId,"Note","Keep linked");
    }
    [Fact] public async Task CorrectionsRetainMeasurementNoteAndLegacyAdjustmentsAcrossRestart()
    {
        await Seed();var game=await Store.LoadProfileAsync(id);
        await Store.SaveStatisticsAsync(game.Statistics.Value! with{Adjustments=[new(Guid.NewGuid(),100,"Legacy")]});
        var journal=File.ReadAllBytes(Path.Combine(Folder,"Sessions",sessionId.ToString("N")+".json"));
        var c=await Store.CorrectSessionAsync(id,sessionId,3600,3000,"Overcounted");
        var history=await Store.LoadHistoryAsync(id);var summary=SessionHistory.Build(history,TimeZoneInfo.Utc);
        Assert.Equal(3100,summary.TotalSeconds);Assert.Equal(3600,summary.MeasuredSeconds);Assert.Equal(3000,summary.Sessions[0].AdjustedSeconds);
        Assert.Equal(100,summary.UndatedAdjustments);Assert.Equal(-600,c.Delta);Assert.Equal(1,c.Sequence);
        Assert.Equal(journal,File.ReadAllBytes(Path.Combine(Folder,"Sessions",sessionId.ToString("N")+".json")));
        Assert.Equal("Keep linked",(await Store.LoadNotesAsync(id)).Sessions[0].Note.Value!.Body);
        await Store.SetGameStatusAsync(id,GameStatus.Completed);
        Assert.Equal(GameStatus.Completed,(await Store.LoadProfileAsync(id)).Profile.Value!.Status);
        await Store.CorrectSessionAsync(id,sessionId,3000,3200,"Second correction");
        Assert.Equal(2,(await Store.LoadHistoryAsync(id)).Corrections.Length);
        await Assert.ThrowsAsync<IOException>(()=>Store.CorrectSessionAsync(id,sessionId,3000,3400,"Stale"));
    }
    [Fact] public async Task FailedProjectionIsRecoveredExactlyOnceWithoutLosingAudit()
    {
        await Seed();var failing=new AppDataStore(ApplicationPaths.FromRoot(root),path=>{if(Path.GetFileName(path)=="statistics.json")throw new IOException("fixture");});
        await Assert.ThrowsAsync<IOException>(()=>failing.CorrectSessionAsync(id,sessionId,3600,3000,"Recover me"));
        Assert.Single(Directory.GetFiles(Path.Combine(Folder,"Corrections"),"*.json"));
        Assert.Equal(DocumentState.Unavailable,(await failing.LoadProfileAsync(id)).Statistics.State);
        for(var i=0;i<2;i++)
        {
            var game=await Store.LoadProfileAsync(id);Assert.Equal(DocumentState.Ready,game.Statistics.State);
            Assert.Single(game.Statistics.Value!.Adjustments);Assert.Equal(-600,game.Statistics.Value.Adjustments[0].Seconds);
        }
    }
    [Fact] public async Task DamagedOrMissingAuditProtectsStatisticsAndBlocksWrites()
    {
        await Seed();var c=await Store.CorrectSessionAsync(id,sessionId,3600,3200,"Reason");
        var file=Path.Combine(Folder,"Corrections",c.Id.ToString("N")+".json");File.WriteAllText(file,"{\"schemaVersion\":99}");
        Assert.Equal(DocumentState.Unavailable,(await Store.LoadProfileAsync(id)).Statistics.State);
        await Assert.ThrowsAsync<IOException>(()=>Store.CorrectSessionAsync(id,sessionId,3200,3400,"Replace"));
        File.Delete(file);Assert.Equal(DocumentState.Unavailable,(await Store.LoadProfileAsync(id)).Statistics.State);
    }
    [Fact] public async Task InvalidActiveAndUnknownSessionsCannotBeCorrected()
    {
        await Seed();
        await Assert.ThrowsAsync<ArgumentException>(()=>Store.CorrectSessionAsync(id,sessionId,3600,-1,"Negative"));
        await Assert.ThrowsAsync<ArgumentException>(()=>Store.CorrectSessionAsync(id,sessionId,3600,1," "));
        await Assert.ThrowsAsync<IOException>(()=>Store.CorrectSessionAsync(id,Guid.NewGuid(),0,1,"Unknown"));
        var active=Guid.NewGuid();var start=DateTimeOffset.Parse("2026-11-12T12:00:00Z");
        await Store.SaveSessionJournalAsync(new(1,id,active,start,start,0,SessionState.Active,13,124,@"D:\Game\game.exe",null,0));
        await Assert.ThrowsAsync<IOException>(()=>Store.CorrectSessionAsync(id,sessionId,3600,3000,"Game running"));
        await Assert.ThrowsAsync<IOException>(()=>Store.SetGameStatusAsync(id,GameStatus.Completed));
    }
    [Fact] public async Task StatisticsWriterCannotDiscardProjectedCorrections()
    {
        await Seed();var old=(await Store.LoadProfileAsync(id)).Statistics.Value!;
        await Store.CorrectSessionAsync(id,sessionId,3600,3000,"Keep");
        await Assert.ThrowsAsync<IOException>(()=>Store.SaveStatisticsAsync(old));
        Assert.Equal(3000,SessionHistory.Build(await Store.LoadHistoryAsync(id),TimeZoneInfo.Utc).TotalSeconds);
    }
    [Fact] public async Task CorrectionCannotOverflowSessionActivityEvenWhenLegacyDeltaOffsetsTotal()
    {
        await Seed();var stats=(await Store.LoadProfileAsync(id)).Statistics.Value!;
        var extra=new PlaySession(Guid.NewGuid(),DateTimeOffset.Parse("2026-11-10T10:00:00Z"),DateTimeOffset.Parse("2026-11-10T11:00:00Z"),3600);
        await Store.SaveStatisticsAsync(stats with{Sessions=[..stats.Sessions,extra],Adjustments=[new(Guid.NewGuid(),-5000,"legacy")]});
        await Assert.ThrowsAsync<OverflowException>(()=>Store.CorrectSessionAsync(id,sessionId,3600,long.MaxValue,"Too much"));
        Assert.False(Directory.Exists(Path.Combine(Folder,"Corrections")));
    }    public void Dispose(){if(Directory.Exists(root))Directory.Delete(root,true);}
}