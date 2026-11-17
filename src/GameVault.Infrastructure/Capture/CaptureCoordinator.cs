using GameVault.Core;
namespace GameVault.Infrastructure.Capture;

public sealed class CaptureCoordinator
{
 readonly SemaphoreSlim gate=new(1,1);
 readonly Func<ICaptureBackend> factory;
 readonly CaptureMediaStore store;
 readonly TimeSpan timeout;
 ICaptureBackend? backend;
 volatile CaptureMedia? active;
 Task? stopTask;
 Task? screenshotWriter;
 public CaptureCoordinator(ApplicationPaths paths,Func<ICaptureBackend> backendFactory,TimeSpan? finalizeTimeout=null)
 {
  store=new(paths);factory=backendFactory;timeout=finalizeTimeout??TimeSpan.FromSeconds(15);
  if(timeout<=TimeSpan.Zero || timeout>TimeSpan.FromMinutes(1))throw new ArgumentOutOfRangeException(nameof(finalizeTimeout));
 }
 public event EventHandler? Changed;
 public Guid? ActiveProfileId=>active?.ProfileId;
 public Task<CaptureMedia[]> ListMediaAsync(Guid profileId)=>store.ListAsync(profileId);
 public CaptureState State{get;private set;}
 public CaptureTarget? ActiveTarget{get;private set;}
 sealed record RecordingClock(DateTimeOffset Value);
 volatile RecordingClock? recordingClock;
 public DateTimeOffset? RecordingStartedUtc=>recordingClock?.Value;
 RecordingWatchdog? watchdog;
 volatile VideoHealth videoHealth=VideoHealth.Unknown;
 /// <summary>Whether the picture of the running recording is still being produced. Call <see cref="PollHealth"/> regularly (about once a second).</summary>
 public VideoHealth VideoHealth=>videoHealth;
 public void PollHealth(DateTimeOffset? now=null)
 {
  var dog=watchdog;
  if(State!=CaptureState.Recording || dog is null || backend is not ICaptureBackendHealth health)return;
  var before=videoHealth;var after=dog.Observe(health.VideoFrames,now??DateTimeOffset.UtcNow);
  videoHealth=after;
  if(after.Stalled!=before.Stalled || after.Episodes!=before.Episodes)NotifyChanged();
 }
 CaptureMedia WithWarning(CaptureMedia media)=>videoHealth.Warning is string warning?media with{Warning=warning}:media;
 public string? LastError{get;private set;}
 public async Task<CaptureMedia> ScreenshotAsync(Guid profileId,CaptureTarget target,CancellationToken cancellationToken=default)
 {
  await gate.WaitAsync(cancellationToken);CaptureMedia? media=null;ICaptureBackend? capture=null;
  using var operationCancellation=CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
  try
  {
   if(screenshotWriter is {IsCompleted:false})throw new InvalidOperationException("The previous screenshot is still finalizing. Restart the launcher if its native writer does not finish.");
   if(active is not null && State!=CaptureState.Recording)throw new InvalidOperationException("Wait for the previous capture to finish finalizing before taking a screenshot.");
   media=await store.BeginAsync(profileId,target,CaptureMediaKind.Screenshot);
   capture=factory();
   screenshotWriter=capture.ScreenshotAsync(target,media.FilePath,operationCancellation.Token);
   await screenshotWriter.WaitAsync(timeout,cancellationToken);
   screenshotWriter=null;
   LastError=null;return store.Complete(media);
  }
  catch(Exception ex)
  {
   operationCancellation.Cancel();LastError=ex.Message;
   if(capture is ICaptureBackendLifetime lifetime)screenshotWriter=lifetime.Completion;
   if(media is not null)store.Fail(media,ex);throw;
  }
  finally{gate.Release();NotifyChanged();}
 }
 public async Task StartAsync(Guid profileId,CaptureTarget target,CaptureOptions? options=null,CancellationToken cancellationToken=default)
 {
  await gate.WaitAsync(cancellationToken);bool attempted=false;
  using var operationCancellation=CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
  try
  {
   if(screenshotWriter is {IsCompleted:false})throw new InvalidOperationException("Wait for the previous screenshot writer to finish before recording.");
   if(active is not null)throw new InvalidOperationException("A capture is already active or awaiting finalization. Restart the launcher after a finalization failure.");
   (options??new()).Validate();attempted=true;
   State=CaptureState.Starting;LastError=null;NotifyChanged();
   active=await store.BeginAsync(profileId,target,CaptureMediaKind.Video);
   ActiveTarget=target;
   watchdog=new();videoHealth=VideoHealth.Unknown;
   backend=factory();
   await backend.StartAsync(target,active.FilePath,options??new(),operationCancellation.Token).WaitAsync(timeout,cancellationToken);
   recordingClock=new(DateTimeOffset.UtcNow);State=CaptureState.Recording;
   if(backend is ICaptureBackendLifetime lifetime)_=ObserveCompletionAsync(lifetime,active);
  }
  catch(Exception ex) when(attempted)
  {
   operationCancellation.Cancel();State=CaptureState.Failed;LastError=ex.Message;
   if(active is not null)store.Fail(active,ex);
   // No backend was created, so no native writer can still own this reservation.
   if(backend is null || backend is ICaptureBackendLifetime { Completion.IsCompleted:true })ClearActive();
   else
   {
    try
    {
     stopTask??=Task.Run(()=>backend.StopAsync(CancellationToken.None));
     await stopTask.WaitAsync(timeout);ClearActive();
    }
    catch(Exception cleanupError)
    {
     System.Diagnostics.Debug.WriteLine(cleanupError);
     if(backend is ICaptureBackendLifetime lifetime && active is not null)_=ObserveFailedStartupAsync(lifetime,active);
    }
   }
   throw;
  }
  finally{gate.Release();NotifyChanged();}
 }
 public async Task<CaptureMedia?> StopAsync(CancellationToken cancellationToken=default)
 {
  await gate.WaitAsync(cancellationToken);
  try
  {
   if(active is null)return null;
   State=CaptureState.Stopping;NotifyChanged();
   if(backend is null)throw new IOException("Capture did not initialize; its incomplete output was preserved.");
   // Cancelling a UI request must not interrupt durable native finalization.
   stopTask??=Task.Run(()=>backend.StopAsync(CancellationToken.None));
   await stopTask.WaitAsync(timeout);
   var complete=store.Complete(WithWarning(active));ClearActive();State=CaptureState.Idle;
   return complete;
  }
  catch(Exception ex)
  {
   State=CaptureState.Failed;LastError=ex.Message;
   if(active is not null)store.Fail(active,ex);
   if(stopTask?.IsCompletedSuccessfully==true || backend is ICaptureBackendLifetime { Completion.IsCompleted:true })ClearActive();
   throw;
  }
  finally{gate.Release();NotifyChanged();}
 }
 async Task ObserveCompletionAsync(ICaptureBackendLifetime lifetime,CaptureMedia media)
 {
  try
  {
   await lifetime.Completion;
   await gate.WaitAsync();
   try
   {
    if(active!=media)return;
    store.Complete(WithWarning(media));ClearActive();State=CaptureState.Idle;
   }
   finally{gate.Release();NotifyChanged();}
  }
  catch(Exception ex)
  {
   await gate.WaitAsync();
   try
   {
    if(active!=media)return;
    State=CaptureState.Failed;LastError=ex.Message;store.Fail(media,ex);ClearActive();
   }
   finally{gate.Release();NotifyChanged();}
  }
 }
 async Task ObserveFailedStartupAsync(ICaptureBackendLifetime lifetime,CaptureMedia media)
 {
  try{await lifetime.Completion;}catch(Exception ex){System.Diagnostics.Debug.WriteLine(ex);}
  await gate.WaitAsync();
  try{if(active==media)ClearActive();}
  finally{gate.Release();NotifyChanged();}
 }
 void ClearActive(){active=null;backend=null;stopTask=null;ActiveTarget=null;recordingClock=null;watchdog=null;videoHealth=VideoHealth.Unknown;}
 void NotifyChanged()
 {
  // A UI subscriber must marshal to its dispatcher. It cannot be allowed to corrupt a media transaction.
  if(Changed is not { } handlers)return;
  foreach(EventHandler handler in handlers.GetInvocationList())
   try{handler(this,EventArgs.Empty);}catch(Exception ex){System.Diagnostics.Debug.WriteLine(ex);}
 }
}
