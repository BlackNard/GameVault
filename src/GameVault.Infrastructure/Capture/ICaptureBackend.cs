using GameVault.Core;
namespace GameVault.Infrastructure.Capture;

public interface ICaptureBackend
{
 Task ScreenshotAsync(CaptureTarget target, string partialPath, CancellationToken cancellationToken);
 Task StartAsync(CaptureTarget target, string partialPath, CaptureOptions options, CancellationToken cancellationToken);
 // Completes only after the native recording-complete callback, not merely after Stop is requested.
 Task StopAsync(CancellationToken cancellationToken);
}

// Optional: how many video frames the recorder has produced, or -1 when it cannot say. Lets GameVault notice a frozen picture.
public interface ICaptureBackendHealth
{
 long VideoFrames { get; }
}

// Optional lifetime observation makes asynchronous native failures visible without waiting for a Stop click.
public interface ICaptureBackendLifetime
{
 Task Completion { get; }
}
