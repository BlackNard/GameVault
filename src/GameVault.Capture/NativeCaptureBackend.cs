using GameVault.Core;
using GameVault.Infrastructure.Capture;
using ScreenRecorderLib;
namespace GameVault.Capture;

internal sealed class NativeCaptureBackend : ICaptureBackend, ICaptureBackendLifetime, ICaptureBackendHealth
{
 NativeOperation? recording;
 NativeOperation? screenshot;
 public long VideoFrames=>recording?.Frames??-1;
 public Task Completion=>recording?.Completion??screenshot?.Completion??Task.CompletedTask;
 public async Task ScreenshotAsync(CaptureTarget target,string partialPath,CancellationToken cancellationToken)
 {
  cancellationToken.ThrowIfCancellationRequested();
  var operation=Create(target,partialPath,new(),true);screenshot=operation;
  await operation.CompleteAsync(cancellationToken);
 }
 public async Task StartAsync(CaptureTarget target,string partialPath,CaptureOptions options,CancellationToken cancellationToken)
 {
  cancellationToken.ThrowIfCancellationRequested();
  if(recording is not null)throw new InvalidOperationException("A recording is already active.");
  recording=Create(target,partialPath,options,false);
  try{await recording.Started.WaitAsync(cancellationToken);}catch{recording.Stop();throw;}
 }
 public async Task StopAsync(CancellationToken cancellationToken)
 {
  var operation=recording;if(operation is null)return;
  operation.Stop();await operation.CompleteAsync(cancellationToken);recording=null;
 }
 static NativeOperation Create(CaptureTarget target,string path,CaptureOptions options,bool screenshot)
 {
  options.Validate();CaptureWindowTarget.ValidateProcess(target);
  RecordingSourceBase source;
  if(target.MonitorDeviceName is not null)
  {
   if(!Recorder.GetDisplays().Any(d=>string.Equals(d.DeviceName,target.MonitorDeviceName,StringComparison.Ordinal)))
    throw new IOException("The explicitly selected monitor is no longer available.");
   source=new DisplayRecordingSource(target.MonitorDeviceName);
  }
  else source=new WindowRecordingSource(CaptureWindowTarget.Resolve(target));
  if(options.Audio==CaptureAudioMode.ProcessOnly && !OperatingSystem.IsWindowsVersionAtLeast(10,0,20348))
   throw new IOException("Process-only game audio requires Windows build 20348 or newer. Choose no audio to continue.");
  var settings=new RecorderOptions
  {
   SourceOptions=new SourceOptions{RecordingSources=[source]},
   OutputOptions=new OutputOptions{RecorderMode=screenshot?RecorderMode.Screenshot:RecorderMode.Video,
    OutputFrameSize=new ScreenSize(options.Width,options.Height),Stretch=StretchMode.Uniform},
   AudioOptions=new AudioOptions{IsAudioEnabled=!screenshot && options.Audio==CaptureAudioMode.ProcessOnly,
    AudioSources=!screenshot && options.Audio==CaptureAudioMode.ProcessOnly?[new ProcessAudioSource(target.ProcessId)]:[]},
   VideoEncoderOptions=new VideoEncoderOptions{Framerate=options.FramesPerSecond,Bitrate=options.Bitrate,
    IsFixedFramerate=true,IsHardwareEncodingEnabled=options.HardwareEncoding,IsThrottlingDisabled=false,
    Encoder=new H264VideoEncoder{BitrateMode=H264BitrateControlMode.UnconstrainedVBR,EncoderProfile=H264Profile.Main}},
   SnapshotOptions=new SnapshotOptions{SnapshotFormat=ImageFormat.PNG,SnapshotsWithVideo=false},
   MouseOptions=new MouseOptions{IsMousePointerEnabled=false,IsMouseClicksDetected=false},
   LogOptions=new LogOptions{IsLogEnabled=false}
  };
  CaptureWindowTarget.ValidateProcess(target);
  return new NativeOperation(settings,path);
 }
 sealed class NativeOperation
 {
  readonly Recorder recorder; readonly FileStream stream;
  readonly TaskCompletionSource started=new(TaskCreationOptions.RunContinuationsAsynchronously);
  readonly TaskCompletionSource completed=new(TaskCreationOptions.RunContinuationsAsynchronously);
  readonly Task cleanup; int stopRequested; long frames;
  public long Frames=>Interlocked.Read(ref frames);
  public Task Started=>started.Task;
  public Task Completion=>cleanup;
  public NativeOperation(RecorderOptions options,string path)
  {
   recorder=Recorder.CreateRecorder(options);
   try
   {
    stream=new FileStream(path,FileMode.Open,FileAccess.ReadWrite,FileShare.Read);
    try{stream.SetLength(0);}catch{stream.Dispose();throw;}
   }
   catch{recorder.Dispose();throw;}
   recorder.OnStatusChanged+=(_,e)=>{if(e.Status==RecorderStatus.Recording)started.TrySetResult();};
   // Only a counter: lets GameVault notice when the picture stops while audio continues. No frame data is read.
   recorder.OnFrameRecorded+=(_,_)=>Interlocked.Increment(ref frames);
   recorder.OnRecordingComplete+=(_,_)=>{started.TrySetResult();completed.TrySetResult();};
   recorder.OnRecordingFailed+=(_,e)=>
   {
    var error=new IOException("Capture failed: "+e.Error+". Windowed/borderless mode or explicit monitor fallback may help; protected/elevated content cannot be bypassed.");
    started.TrySetException(error);completed.TrySetException(error);
   };
   cleanup=CleanupAsync();
   try{recorder.Record(stream);}
   catch(Exception ex){started.TrySetException(ex);completed.TrySetException(ex);}
  }
  async Task CleanupAsync()
  {
   try{await completed.Task;}finally
   {
    // Never dispose inside a native callback; its thread must return first.
    await Task.Yield();
    try{recorder.Dispose();}finally{try{stream.Flush(true);}finally{stream.Dispose();}}
   }
  }
  public void Stop(){if(Interlocked.Exchange(ref stopRequested,1)==0)try{recorder.Stop();}catch(Exception ex){completed.TrySetException(ex);}}
  public async Task CompleteAsync(CancellationToken cancellationToken)
  {
   using var registration=cancellationToken.Register(()=>Stop());
   await cleanup.WaitAsync(cancellationToken);
  }
 }
}
