using GameVault.Core;
namespace GameVault.Tests;
public sealed class TrayPolicyTests
{
    [Fact] public async Task HideDoesNotPrepareOrQuitAndReopenKeepsRuntimeAlive()
    {
        int hidden=0,opened=0,prepared=0,quit=0;var lifetime=new TrayLifetime(()=>hidden++,()=>opened++,()=>{prepared++;return Task.FromResult(true);},()=>{quit++;return Task.CompletedTask;});
        Assert.Equal(WindowCloseResult.Hidden,await lifetime.CloseAsync(true,true));lifetime.Reopen();Assert.Equal(1,hidden);Assert.Equal(1,opened);Assert.Equal(0,prepared);Assert.Equal(0,quit);
    }
    [Fact] public async Task MissingTrayNeverHidesAndCancelledQuitLeavesRuntimeAlive()
    {
        int hidden=0,opened=0,quit=0;var lifetime=new TrayLifetime(()=>hidden++,()=>opened++,()=>Task.FromResult(false),()=>{quit++;return Task.CompletedTask;});
        Assert.Equal(WindowCloseResult.StayedOpen,await lifetime.CloseAsync(true,false));Assert.Equal(0,hidden);Assert.Equal(1,opened);
        Assert.Equal(WindowCloseResult.StayedOpen,await lifetime.QuitAsync());Assert.Equal(0,quit);
    }
    [Fact] public async Task ConcurrentQuitRequestsOnlyPrepareAndDisposeOnce()
    {
        int prepares=0,quits=0;var wait=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var lifetime=new TrayLifetime(()=>{},()=>{},()=>{prepares++;return wait.Task;},()=>{quits++;return Task.CompletedTask;});var a=lifetime.QuitAsync();var b=lifetime.CloseAsync(false,true);wait.SetResult(true);
        Assert.Equal(WindowCloseResult.Quit,await a);Assert.Equal(WindowCloseResult.Quit,await b);Assert.Equal(1,prepares);Assert.Equal(1,quits);Assert.Equal(WindowCloseResult.Quit,await lifetime.QuitAsync());Assert.Equal(1,quits);
    }
    [Fact] public void FailureAlertsWaitForPlayThenDeduplicateUntilSuccess()
    {
        var policy=new BackupAlertPolicy();var id=Guid.NewGuid();var now=DateTimeOffset.UtcNow;var failed=new BackupResult(id,BackupPhase.Failed,"locked");
        policy.Record(failed,"Example",now);Assert.Null(policy.TakeNotification(now,true,true));Assert.NotNull(policy.TakeNotification(now,true,false));
        policy.Record(failed,"Example",now.AddMinutes(3));Assert.Null(policy.TakeNotification(now.AddMinutes(3),true,false));Assert.Single(policy.Alerts);
        policy.Record(new(id,BackupPhase.Complete,"verified"),"Example",now.AddMinutes(4));Assert.Empty(policy.Alerts);
        policy.Record(failed,"Example",now.AddMinutes(6));Assert.NotNull(policy.TakeNotification(now.AddMinutes(6),true,false));
    }
    [Fact] public void DisabledAlertsStayReviewableAndGlobalCooldownPreventsBursts()
    {
        var policy=new BackupAlertPolicy();var now=DateTimeOffset.UtcNow;var a=Guid.NewGuid();var b=Guid.NewGuid();policy.Record(new(a,BackupPhase.Failed,"missing"),"One",now);Assert.Null(policy.TakeNotification(now,false,false));Assert.Single(policy.Alerts);
        policy.Record(new(b,BackupPhase.Failed,"denied"),"Two",now);Assert.NotNull(policy.TakeNotification(now,true,false));Assert.Null(policy.TakeNotification(now.AddSeconds(10),true,false));Assert.NotNull(policy.TakeNotification(now.AddMinutes(3),true,false));
        policy.Acknowledge(a);policy.Acknowledge(b);Assert.Empty(policy.Alerts);
    }
    [Fact] public async Task CancelledQuitCanBeRetriedWithoutHidingOrDuplicateShutdown()
    {
        var allow=false;int quits=0;var lifetime=new TrayLifetime(()=>throw new InvalidOperationException("Must not hide"),()=>{},()=>Task.FromResult(allow),()=>{quits++;return Task.CompletedTask;});
        Assert.Equal(WindowCloseResult.StayedOpen,await lifetime.QuitAsync());allow=true;Assert.Equal(WindowCloseResult.Quit,await lifetime.QuitAsync());Assert.Equal(1,quits);
    }
    [Fact] public void AChangedFailureRemainsActionableAfterAcknowledgement()
    {
        var policy=new BackupAlertPolicy();var id=Guid.NewGuid();var now=DateTimeOffset.UtcNow;policy.Record(new(id,BackupPhase.Failed,"missing"),"Game",now);policy.TakeNotification(now,true,false);policy.Acknowledge(id);
        policy.Record(new(id,BackupPhase.Failed,"denied"),"Game",now.AddMinutes(3));Assert.Single(policy.Alerts);Assert.NotNull(policy.TakeNotification(now.AddMinutes(3),true,false));
    }
    [Fact] public void QueueIsBoundedAndNonFailuresDoNotGenerateAlerts()
    {
        var policy=new BackupAlertPolicy();var now=DateTimeOffset.UtcNow;for(int i=0;i<140;i++)policy.Record(new(Guid.NewGuid(),BackupPhase.Failed,"failed"),"Game",now);
        Assert.Equal(128,policy.Alerts.Length);policy.Record(new(Guid.NewGuid(),BackupPhase.Working,"copying"),"Working",now);Assert.Equal(128,policy.Alerts.Length);
    }
}