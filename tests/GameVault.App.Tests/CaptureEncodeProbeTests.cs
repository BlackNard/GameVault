using System.IO;
using System.Collections;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Buffers.Binary;
namespace GameVault.App.Tests;

// Opt-in exclusively on a disposable GitHub runner. Never capture an owner's desktop or access audio.
public sealed class CaptureEncodeProbeTests
{
 [CaptureEncodeProbeFact]
 public async Task PinnedNativeLibraryEncodesOnlySyntheticImageAndFinalizesMp4()
 {
  Assert.Equal(8,IntPtr.Size);
  var root=Path.Combine(Path.GetTempPath(),"GameVault-image-encode-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
  var image=Path.Combine(root,"synthetic.png");var partial=Path.Combine(root,"synthetic.mp4.partial");
  File.WriteAllBytes(image,Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAEAAAABACAYAAACqaXHeAAAAmElEQVR4nO3QMREAIBDAsLeBIXTiFGRkoEP2Xmftc382OkBrgA7QGqADtAboAK0BOkBrgA7QGqADtAboAK0BOkBrgA7QGqADtAboAK0BOkBrgA7QGqADtAboAK0BOkBrgA7QGqADtAboAK0BOkBrgA7QGqADtAboAK0BOkBrgA7QGqADtAboAK0BOkBrgA7QGqADtAboAO0B4hHh0qeoeVMAAAAASUVORK5CYII="));
  var assembly=Assembly.LoadFrom(Path.Combine(AppContext.BaseDirectory,"ScreenRecorderLib.dll"));
  object New(string name,params object[] args)=>Activator.CreateInstance(assembly.GetType("ScreenRecorderLib."+name,true)!,args)!;
  object EnumValue(string name,string value)=>Enum.Parse(assembly.GetType("ScreenRecorderLib."+name,true)!,value);
  void Set(object value,string property,object setting)=>value.GetType().GetProperty(property)!.SetValue(value,setting);
  object Get(object value,string property)=>value.GetType().GetProperty(property)!.GetValue(value)!;
  var options=New("RecorderOptions");var sources=New("SourceOptions");
  ((IList)Get(sources,"RecordingSources")).Add(New("ImageRecordingSource",image));Set(options,"SourceOptions",sources);
  var output=New("OutputOptions");Set(output,"RecorderMode",EnumValue("RecorderMode","Video"));
  Set(output,"OutputFrameSize",New("ScreenSize",64,64));Set(options,"OutputOptions",output);
  var audio=New("AudioOptions");Set(audio,"IsAudioEnabled",false);((IList)Get(audio,"AudioSources")).Clear();Set(options,"AudioOptions",audio);
  var encoder=New("VideoEncoderOptions");Set(encoder,"Framerate",30);Set(encoder,"Bitrate",1_000_000);
  Set(encoder,"IsHardwareEncodingEnabled",false);Set(encoder,"IsFixedFramerate",true);Set(encoder,"Encoder",New("H264VideoEncoder"));Set(options,"VideoEncoderOptions",encoder);
  var mouse=New("MouseOptions");Set(mouse,"IsMousePointerEnabled",false);Set(mouse,"IsMouseClicksDetected",false);Set(options,"MouseOptions",mouse);
  var recorderType=assembly.GetType("ScreenRecorderLib.Recorder",true)!;
  var recorder=recorderType.GetMethod("CreateRecorder",[options.GetType()])!.Invoke(null,[options])!;
  var started=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
  var completed=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
  Delegate Subscribe(string name,Action<object?,object?> action)
  {
   var info=recorderType.GetEvent(name)!;var signature=info.EventHandlerType!.GetMethod("Invoke")!;
   var args=signature.GetParameters().Select(p=>Expression.Parameter(p.ParameterType,p.Name)).ToArray();
   var handler=Expression.Lambda(info.EventHandlerType,Expression.Invoke(Expression.Constant(action),args.Select(a=>Expression.Convert(a,typeof(object)))),args).Compile();
   info.AddEventHandler(recorder,handler);return handler;
  }
  var statusHandler=Subscribe("OnStatusChanged",(_,e)=>{if(e!.GetType().GetProperty("Status")!.GetValue(e)!.ToString()=="Recording")started.TrySetResult();});
  var completeHandler=Subscribe("OnRecordingComplete",(_,_)=>completed.TrySetResult());
  var failureHandler=Subscribe("OnRecordingFailed",(_,e)=>
  {
   var error=new IOException("Native image-only encode failed: "+e!.GetType().GetProperty("Error")!.GetValue(e));
   started.TrySetException(error);completed.TrySetException(error);
  });
  var stream=new FileStream(partial,FileMode.CreateNew,FileAccess.ReadWrite,FileShare.Read);
  var finalized=false;
  try
  {
   recorderType.GetMethod("Record",[typeof(Stream)])!.Invoke(recorder,[stream]);
   await started.Task.WaitAsync(TimeSpan.FromSeconds(30));
   await Task.Delay(1200);
   recorderType.GetMethod("Stop")!.Invoke(recorder,null);
   await completed.Task.WaitAsync(TimeSpan.FromSeconds(30));finalized=true;
   await Task.Run(()=>((IDisposable)recorder).Dispose()).WaitAsync(TimeSpan.FromSeconds(10));
   stream.Flush(true);stream.Dispose();
   var bytes=File.ReadAllBytes(partial);Assert.True(bytes.Length>128);
   AssertFinalizedMp4(bytes);
   File.Move(partial,partial[..^8]);Assert.True(File.Exists(partial[..^8]));
  }
  finally
  {
   GC.KeepAlive(statusHandler);GC.KeepAlive(completeHandler);GC.KeepAlive(failureHandler);
   if(finalized){stream.Dispose();Directory.Delete(root,true);}
   // On native hangs, retain the incomplete fixture. The disposable runner process owns cleanup.
  }
 }
 // Parse top-level ISO BMFF boxes: strings inside compressed payload are not evidence of finalization.
 private static void AssertFinalizedMp4(byte[] bytes)
 {
  var boxes = new Dictionary<string, ulong>();
  var offset = 0;
  while (offset < bytes.Length)
  {
   Assert.True(bytes.Length - offset >= 8, "Truncated MP4 box header.");
   ulong size = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset, 4));
   var type = Encoding.ASCII.GetString(bytes, offset + 4, 4);
   var header = 8;
   if (size == 1)
   {
    Assert.True(bytes.Length - offset >= 16, "Truncated extended MP4 box header.");
    size = BinaryPrimitives.ReadUInt64BigEndian(bytes.AsSpan(offset + 8, 8));
    header = 16;
   }
   else if (size == 0) size = (ulong)(bytes.Length - offset);
   Assert.True(size >= (ulong)header && size <= (ulong)(bytes.Length - offset), "Invalid MP4 box size.");
   if (offset == 0) Assert.Equal("ftyp", type);
   boxes[type] = size - (ulong)header;
   offset += checked((int)size);
  }
  Assert.True(boxes.TryGetValue("moov", out var metadata) && metadata > 0, "Missing finalized movie metadata.");
  Assert.True(boxes.TryGetValue("mdat", out var media) && media > 0, "Missing encoded video samples.");
 }

}
public sealed class CaptureEncodeProbeFactAttribute : FactAttribute
{
 public CaptureEncodeProbeFactAttribute()
 {
  if(!OperatingSystem.IsWindows() || Environment.GetEnvironmentVariable("GITHUB_ACTIONS")!="true" ||
     Environment.GetEnvironmentVariable("RUNNER_ENVIRONMENT")!="github-hosted" || Environment.GetEnvironmentVariable("RUNNER_OS")!="Windows" ||
     Environment.GetEnvironmentVariable("GAMEVAULT_CAPTURE_ENCODE_PROBE")!="1")
   Skip="Native image-only encode probe runs only on an explicitly enabled disposable GitHub runner; no owner-device capture is permitted.";
 }
}
