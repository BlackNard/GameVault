using GameVault.Core;
namespace GameVault.Tests;
public sealed class HistoryTests
{
    private static PlaySession Session(string start, string? end, long seconds) => new(Guid.NewGuid(), DateTimeOffset.Parse(start), end is null ? null : DateTimeOffset.Parse(end), seconds);
    [Fact] public void MidnightSplitPreservesExactSecondsWithDeterministicRounding()
    {
        var s = Session("2026-11-11T23:40:00Z", "2026-11-12T01:02:10Z", 4930);
        var days = SessionHistory.AllocateDays(s, 4930, TimeZoneInfo.Utc);
        Assert.Equal(new[]{new ActivityBucket(new(2026,11,11),1200),new ActivityBucket(new(2026,11,12),3730)}, days);
        var rounded = SessionHistory.AllocateDays(Session("2026-11-11T23:59:59Z", "2026-11-12T00:00:02Z", 2),2,TimeZoneInfo.Utc);
        Assert.Equal(new long[]{0,2},rounded.Select(d=>d.Seconds));
        Assert.Equal(2,rounded.Sum(d=>d.Seconds));
    }
    [Fact] public void DstDaysUseRealUtcBoundariesAndWeekMonthKeys()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
        var days = SessionHistory.AllocateDays(Session("2026-03-08T05:00:00Z","2026-03-10T04:00:00Z",169200),169200,zone);
        Assert.Equal(new long[]{82800,86400},days.Select(d=>d.Seconds));
        var fall = SessionHistory.AllocateDays(Session("2026-11-01T04:00:00Z","2026-11-03T05:00:00Z",176400),176400,zone);
        Assert.Equal(new long[]{90000,86400},fall.Select(d=>d.Seconds));
        Assert.Equal(new DateOnly(2026,12,28),SessionHistory.PeriodStart(new(2027,1,3),ActivityGrouping.Week));
        Assert.Equal(new DateOnly(2027,1,1),SessionHistory.PeriodStart(new(2027,1,3),ActivityGrouping.Month));
    }
    [Fact] public void MissingIntervalsStayUnallocatedAndZeroIntervalsKeepTheirDuration()
    {
        Assert.Empty(SessionHistory.AllocateDays(Session("2026-11-12T10:00:00Z",null,100),100,TimeZoneInfo.Utc));
        Assert.Equal(100,Assert.Single(SessionHistory.AllocateDays(Session("2026-11-12T10:00:00Z","2026-11-12T10:00:00Z",100),100,TimeZoneInfo.Utc)).Seconds);
    }
    [Fact] public void SummaryKeepsMeasuredAndCorrectedTimesAndExcludesActiveLongest()
    {
        var id=Guid.NewGuid();var a=Session("2026-11-11T10:00:00Z","2026-11-11T11:00:00Z",3600);var active=Session("2026-11-12T10:00:00Z",null,8000);
        var correction=new TimeCorrection(1,Guid.NewGuid(),id,a.Id,1,3600,3600,4000,"Correction",DateTimeOffset.UtcNow);
        var stats=new GameStatistics(1,id,[a,active],[new(correction.Id,400,"linked"),new(Guid.NewGuid(),-50,"legacy")]);
        var data=new HistoryData(new(id,new(null,DocumentState.Missing),new(stats,DocumentState.Ready)),[],[correction],[]);
        var summary=SessionHistory.Build(data,TimeZoneInfo.Utc);
        Assert.Equal(11950,summary.TotalSeconds);Assert.Equal(11600,summary.MeasuredSeconds);Assert.Equal(-50,summary.UndatedAdjustments);
        Assert.Equal(8000,summary.UnallocatedSessionSeconds);Assert.Equal(a.Id,summary.LongestSessionId);
        Assert.Equal(4000,summary.Sessions.Single(s=>s.Session.Id==a.Id).AdjustedSeconds);
        Assert.Equal(SessionEndKind.Active,summary.Sessions[0].EndKind);Assert.Equal(active.StartedAt,summary.LastPlayed);
        Assert.Equal(4000,summary.Days.Sum(d=>d.Seconds));
    }
    [Fact] public void InterruptedAndUnknownEndsRemainDistinctFromVerifiedExit()
    {
        var id=Guid.NewGuid();var s=Session("2026-11-12T10:00:00Z","2026-11-12T11:00:00Z",3000);
        var j=new SessionJournal(1,id,s.Id,s.StartedAt,s.EndedAt!.Value,3000*TimeSpan.TicksPerSecond,SessionState.Interrupted,1,1,@"D:\game.exe",null,1);
        var data=new HistoryData(new(id,new(null,DocumentState.Missing),new(new(1,id,[s],[]),DocumentState.Ready)),[new(s.Id,new(j,DocumentState.Ready))],[],[]);
        var row=Assert.Single(SessionHistory.Build(data,TimeZoneInfo.Utc).Sessions);
        Assert.Equal(SessionEndKind.Interrupted,row.EndKind);Assert.False(row.CanCorrect);Assert.True(row.Recovered);
        Assert.Equal(SessionEndKind.Unknown,SessionHistory.Build(data with { Journals=[] },TimeZoneInfo.Utc).Sessions[0].EndKind);
        var empty=SessionHistory.Build(data with{Game=data.Game with{Statistics=new(GameStatistics.Empty(id),DocumentState.Ready)},Journals=[]},TimeZoneInfo.Utc);
        Assert.Equal(0,empty.TotalSeconds);Assert.Null(empty.LongestSessionId);Assert.Null(empty.LastPlayed);
    }
}