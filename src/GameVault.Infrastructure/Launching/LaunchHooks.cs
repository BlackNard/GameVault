using System.ComponentModel;
using System.Diagnostics;
using GameVault.Core;

namespace GameVault.Infrastructure.Launching;

public sealed record HookOutcome(string Label, bool Succeeded, string Message);
public sealed record LaunchHookResult(Guid GameId, HookOutcome Outcome);

/// <summary>Runs the commands the owner configured before a game starts and after it exits. A hook can fail or time out; the game and playtime tracking carry on regardless.</summary>
public interface ILaunchHookRunner
{
    Task<HookOutcome> RunAsync(string label, LaunchHook hook, CancellationToken cancellationToken);
}

public sealed class ProcessHookRunner : ILaunchHookRunner
{
    public async Task<HookOutcome> RunAsync(string label, LaunchHook hook, CancellationToken cancellationToken)
    {
        if (!LaunchRules.IsValidHook(hook) || !File.Exists(hook.ExecutablePath))
            return new(label, false, "The program was not found or the hook settings are invalid, so it was not run.");
        try
        {
            // No shell: the program is started directly and the arguments are plain text.
            var start = new ProcessStartInfo(hook.ExecutablePath, hook.Arguments) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(hook.ExecutablePath)! };
            using var process = Process.Start(start) ?? throw new IOException("Windows did not start the program.");
            if (!hook.WaitForExit) return new(label, true, "Started.");
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            limit.CancelAfter(TimeSpan.FromSeconds(hook.TimeoutSeconds));
            try { await process.WaitForExitAsync(limit.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // Only the hook this runner started is stopped; never the game or GameVault.
                try { if (!process.HasExited) process.Kill(); } catch (Exception error) when (error is InvalidOperationException or Win32Exception) { }
                return new(label, false, $"It did not finish within {hook.TimeoutSeconds} seconds and was stopped.");
            }
            return process.ExitCode == 0 ? new(label, true, "Finished.") : new(label, false, $"It finished with exit code {process.ExitCode}.");
        }
        catch (Exception error) when (error is Win32Exception or IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            return new(label, false, error.Message);
        }
    }
}
