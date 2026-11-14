using System.Runtime.CompilerServices;
using GameVault.Core;
using GameVault.Infrastructure.Capture;
namespace GameVault.Capture;

// No ScreenRecorderLib types in this public wrapper. Native prerequisites are touched on demand only.
public sealed class WindowsCaptureBackend : ICaptureBackend, ICaptureBackendLifetime
{
 ICaptureBackend? native;
 Task? request;
 public Task Completion=>ObserveRequestAsync(request);
 async Task ObserveRequestAsync(Task? pending)
 {
  Exception? requestError=null;
  try{if(pending is not null)await pending;}catch(Exception ex){requestError=ex;}
  if(native is ICaptureBackendLifetime lifetime)await lifetime.Completion;
  if(requestError is not null)System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(requestError).Throw();
 }
 [MethodImpl(MethodImplOptions.NoInlining)]
 static ICaptureBackend CreateNative()=>new NativeCaptureBackend();
 ICaptureBackend Native=>native??=CreateNative();
 public async Task ScreenshotAsync(CaptureTarget target,string partialPath,CancellationToken cancellationToken)
 {
  try {request=Task.Run(()=>Native.ScreenshotAsync(target,partialPath,cancellationToken),cancellationToken);await request;}
  catch(Exception ex) when(IsDependencyFailure(ex)){throw DependencyError(ex);}
 }
 public async Task StartAsync(CaptureTarget target,string partialPath,CaptureOptions options,CancellationToken cancellationToken)
 {
  try {request=Task.Run(()=>Native.StartAsync(target,partialPath,options,cancellationToken),cancellationToken);await request;}
  catch(Exception ex) when(IsDependencyFailure(ex)){throw DependencyError(ex);}
 }
 public Task StopAsync(CancellationToken cancellationToken)=>Task.Run(()=>native?.StopAsync(cancellationToken)??Task.CompletedTask,cancellationToken);
 static bool IsDependencyFailure(Exception ex)=>ex is DllNotFoundException or BadImageFormatException or FileNotFoundException or TypeInitializationException or TypeLoadException;
 static IOException DependencyError(Exception ex)=>new("Capture could not load its Windows components. Install the Microsoft Visual C++ x64 runtime and Windows Media Foundation (Media Feature Pack on N/KN editions), then restart GameVault. Your library is unaffected.",ex);
}
