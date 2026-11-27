using System.ComponentModel;
using GameVault.Core;

namespace GameVault.Infrastructure.Launching;

// Stop (issue #113). Observation and quitting still never close a game: only this, on the player's request.
public sealed partial class LaunchService
{
    private const int AccessDenied = 5, PermissionDeclined = 1223;
    private readonly Dictionary<Guid, (string TargetPath, IGameProcess Starter)> tracked = [];
    private readonly HashSet<Guid> stopping = [];
    private readonly TimeSpan stopWait;

    /// <summary>Asks a tracked game to close (<paramref name="force"/> false) or ends it, with the programs it started. The session then ends the
    /// usual way, so playtime is recorded and the save backup runs.</summary>
    public async Task<StopResult> StopAsync(Guid id, bool force, CancellationToken token = default)
    {
        string targetPath; IGameProcess starter; Task finished;
        lock (gate)
        {
            if (!tracked.TryGetValue(id, out var game) || !active.TryGetValue(id, out var session)) return new(StopOutcome.NotRunning, "That game is not running.");
            if (!stopping.Add(id)) return new(StopOutcome.Cancelled, "GameVault is already stopping that game.");
            (targetPath, starter, finished) = (game.TargetPath, game.Starter, session.Task);
        }
        try
        {
            if (processes is not IGameProcessControl control) return new(StopOutcome.Failed, "This game cannot be stopped from GameVault. Close it in the game.");
            // A second pass for a forced stop catches a program started while the first one was ending.
            for (var pass = 0; pass < (force ? 2 : 1) && !finished.IsCompleted; pass++)
            {
                var asked = 0;
                var denied = new List<ProcessIdentity>();
                foreach (var process in Family(control, targetPath, starter))
                {
                    try
                    {
                        if (force) { control.End(process); asked++; }
                        else if (control.RequestClose(process)) asked++;
                    }
                    catch (Win32Exception error) when (error.NativeErrorCode == AccessDenied) { denied.Add(process); }
                }
                if (denied.Count > 0) { await control.EndElevatedAsync(force, [.. denied], token).ConfigureAwait(false); asked += denied.Count; }
                if (asked == 0 && !force) return new(StopOutcome.StillRunning, "The game has no window that can be asked to close.");
                if (await Task.WhenAny(finished, Task.Delay(stopWait, token)).ConfigureAwait(false) == finished) break;
                token.ThrowIfCancellationRequested();
            }
            if (finished.IsCompleted) return new(StopOutcome.Closed, "Game stopped.");
            return force ? new(StopOutcome.Failed, "Windows did not end the game. Close it from the game or from Task Manager.")
                : new(StopOutcome.StillRunning, "The game is still open.");
        }
        catch (Win32Exception error) when (error.NativeErrorCode == PermissionDeclined)
        { return new(StopOutcome.Cancelled, "Permission was not given. The game was left open."); }
        catch (Exception error) when (error is Win32Exception or IOException or UnauthorizedAccessException or InvalidOperationException)
        { return new(StopOutcome.Failed, "The game could not be stopped. " + error.Message); }
        finally { lock (gate) stopping.Remove(id); }
    }

    /// <summary>The game's own processes: the one GameVault started or found, the game executable it started, and their helpers.</summary>
    private ProcessIdentity[] Family(IGameProcessControl control, string targetPath, IGameProcess starter)
    {
        var running = control.Running();
        var targets = processes.Find(targetPath).Select(candidate => candidate.Identity).ToHashSet();
        var family = new List<ProcessIdentity>();
        foreach (var process in running)
        {
            var identity = process.Candidate.Identity;
            if (identity.Id == Environment.ProcessId) continue;
            if (identity != starter.Identity)
            {
                // Cheap checks first: nothing started before the game, and no other program it may merely have opened, belongs to it.
                if (identity.CreatedUtcTicks < starter.Identity.CreatedUtcTicks || StopRules.IsSpared(process.Name) && !targets.Contains(identity)) continue;
                try { if (!starter.Owns(process.Candidate)) continue; }
                catch (Win32Exception) { continue; }              // a process Windows will not let us look at is not ours
            }
            family.Add(identity);
        }
        return [.. family];
    }
}
