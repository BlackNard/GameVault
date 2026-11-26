using GameVault.Core;
using GameVault.Infrastructure.Storage;
using GameVault.Infrastructure.Library;
using GameVault.Infrastructure.Saves;
using System.ComponentModel;
using System.Diagnostics;
using GameVault.Infrastructure.Playtime;

namespace GameVault.Infrastructure.Launching;

public sealed class LaunchService : IAsyncDisposable, IGameSessionSignals
{
    private readonly AppDataStore store;
    private readonly LaunchSettingsStore settings;
    private readonly LibraryService library;
    private readonly IGameProcessSystem processes;
    private readonly TimeSpan startupTimeout;
    private readonly TimeSpan storeStartupTimeout;
    private readonly IStoreGameLauncher storeLauncher;
    private readonly TimeSpan pollInterval;
    private readonly object gate = new();
    private readonly CancellationTokenSource shutdown = new();
    private readonly Dictionary<Guid, TaskCompletionSource> active = [];
    private readonly Dictionary<Guid, LaunchState> states = [];
    private readonly Dictionary<string, Guid> reservedPaths = new(StringComparer.OrdinalIgnoreCase);
    private bool disposed;
    private readonly SessionRecorder recorder;
    private readonly SemaphoreSlim recoveryGate = new(1, 1);
    private bool recoveryCompleted;
    private readonly ILaunchHookRunner hooks;

    public LaunchService(ApplicationPaths paths, AppDataStore store, IGameProcessSystem processes,
        TimeSpan? startupTimeout = null, TimeSpan? pollInterval = null, IPlaytimeClock? clock = null, TimeSpan? storeStartupTimeout = null, IStoreGameLauncher? storeLauncher = null, ILaunchHookRunner? hooks = null)
    {
        this.hooks = hooks ?? new ProcessHookRunner();
        this.store = store; this.processes = processes;
        settings = new(paths, store); library = new(paths, store);
        this.startupTimeout = startupTimeout ?? TimeSpan.FromSeconds(30);
        this.storeStartupTimeout = storeStartupTimeout ?? TimeSpan.FromMinutes(2);
        this.storeLauncher = storeLauncher ?? new StoreGameLauncher();
        this.pollInterval = pollInterval ?? TimeSpan.FromMilliseconds(250);
        recorder = new(store, clock ?? new PlaytimeClock());
    }

    public event EventHandler<GameSessionEnded>? SessionEnded;
    private void NotifySessionEnded(Guid id,bool confirmed)=>SessionEnded?.Invoke(this,new(id,confirmed));
    public event EventHandler<LaunchState>? StateChanged;
    /// <summary>Raised when a before-launch or after-exit hook finishes, so the app can say what happened.</summary>
    public event EventHandler<LaunchHookResult>? HookCompleted;
    public event EventHandler<SessionProgress>? SessionUpdated;
    public SessionProgress? GetSessionProgress(Guid id) => recorder.GetProgress(id);
    internal Task SaveCheckpointAsync(Guid id) => recorder.SampleAsync(id, true);
    public LaunchState[] GetActiveStates(){lock(gate)return states.Values.Where(s=>s.IsActive).ToArray();}
    public async Task<string[]> CheckpointActiveAsync()
    {
        var warnings=new List<string>();
        foreach(var state in GetActiveStates())
        {
            try
            {
                await recorder.SampleAsync(state.GameId,true).ConfigureAwait(false);var progress=recorder.GetProgress(state.GameId);
                if(progress?.Warning is string warning)warnings.Add(warning);
                else if((progress is null||!progress.IsActive)&&GetState(state.GameId).IsActive)warnings.Add("A starting or unrecorded session has no saved timing checkpoint yet.");
            }
            catch(Exception error)when(error is IOException or UnauthorizedAccessException or InvalidOperationException or OverflowException){warnings.Add("The latest session checkpoint could not be saved. "+error.Message);}
        }
        return warnings.Distinct().ToArray();
    }
    public LaunchState GetState(Guid id) { lock (gate) return states.GetValueOrDefault(id) ?? new(id, LaunchPhase.Stopped, "Not running"); }

    private readonly GameOperationLeases operations=new();
    public Task WaitForIdleAsync(Guid id){lock(gate)return active.TryGetValue(id,out var task)?task.Task:Task.CompletedTask;}
    public async Task<bool> IsGameRunningAsync(Guid id)
    {
        lock(gate){if(active.ContainsKey(id))return true;}
        var game=await store.LoadProfileAsync(id).ConfigureAwait(false);
        if(game.Profile.State!=DocumentState.Ready||game.Profile.Value is not GameProfile profile)throw new IOException("Cannot check a protected game profile for running processes.");
        var config=await settings.LoadAsync(id).ConfigureAwait(false);
        if(config.IsReadOnly)throw new IOException(config.Message);
        var plan = storeLauncher.Resolve(profile.ExecutablePath, config.Value?.TargetExecutablePath);
        return new[]{profile.ExecutablePath, plan?.TargetExecutablePath ?? config.Value?.TargetExecutablePath ?? profile.ExecutablePath}.Distinct(StringComparer.OrdinalIgnoreCase).Any(path=>processes.Find(path).Length>0);
    }
    /// <summary>Starts a game. actionId picks a play action; Guid.Empty means the game itself even if a default action is set; null uses the default action.</summary>
    public Task RunAsync(Guid id, Guid? actionId = null)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        IDisposable operation;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            operation=operations.TryAcquire(id)??throw new InvalidOperationException("A game session or backup is active. Wait for it to finish before playing.");
            if (!active.TryAdd(id, completion)){operation.Dispose();throw new InvalidOperationException("This game is already starting or running.");}
        }
        SetState(new(id, LaunchPhase.Starting, "Starting game…"));
        _ = Task.Run(() => RunCore(id, completion, operation, actionId));
        return completion.Task;
    }

    private async Task RunCore(Guid id, TaskCompletionSource completion, IDisposable operation, Guid? actionId = null)
    {
        var token = shutdown.Token;
        try
        {
            var game = await store.LoadProfileAsync(id).ConfigureAwait(false);
            if (game.Profile.IsReadOnly || game.Profile.Value is not GameProfile profile || profile.IsArchived)
                throw new IOException("This game profile is missing, removed, or read-only. It cannot be launched safely.");
            await store.RequireNoPendingRestoreAsync(id).ConfigureAwait(false);
            ValidateExecutable(profile.ExecutablePath);
            if (!Directory.Exists(profile.WorkingDirectory)) throw new IOException("The working directory is missing. Repair it through Edit game.");
            var configuration = await settings.LoadAsync(id).ConfigureAwait(false);
            if (configuration.IsReadOnly) throw new IOException(configuration.Message);
            var action = actionId == Guid.Empty ? null : actionId is null ? LaunchRules.DefaultAction(configuration.Value) : LaunchRules.Find(configuration.Value, actionId)
                ?? throw new IOException("That play action no longer exists. Check Launch settings.");
            var resolvedPlan = storeLauncher.Resolve(profile.ExecutablePath, configuration.Value?.TargetExecutablePath);
            // A custom play action starts its own program; only the plain game uses the store launch request.
            var storePlan = action is null ? resolvedPlan : null;
            var targetPath = resolvedPlan?.TargetExecutablePath ?? configuration.Value?.TargetExecutablePath ?? profile.ExecutablePath;
            ValidateExecutable(targetPath);
            if (action is not null)
            {
                ValidateExecutable(action.ExecutablePath);
                if (action.WorkingDirectory.Length > 0 && !Directory.Exists(action.WorkingDirectory)) throw new IOException($"The working folder of \"{action.Name}\" is missing. Check Launch settings.");
            }
            var startPath = action?.ExecutablePath ?? profile.ExecutablePath;
            var startFolder = action is { WorkingDirectory.Length: > 0 } ? action.WorkingDirectory : profile.WorkingDirectory;
            var paths = new[] { Path.GetFullPath(profile.ExecutablePath), Path.GetFullPath(targetPath), Path.GetFullPath(startPath) }.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var reservations = storePlan is null ? paths : paths.Append("store:" + storePlan.StoreKey).ToArray();
            lock (gate)
            {
                if (reservations.Any(path => reservedPaths.ContainsKey(path))) throw new InvalidOperationException("This executable is already starting or running for another game.");
                foreach (var path in reservations) reservedPaths.Add(path, id);
            }
            foreach (var path in paths)
                if (processes.Find(path).Length > 0) throw new InvalidOperationException("This game or its launcher is already running. Close it before starting another session.");
            token.ThrowIfCancellationRequested();
            await RunHookAsync(id, "Before launch", configuration.Value?.BeforeLaunch, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (storePlan is not null)
            {
                // A running store client can own an unrelated, detached process tree.
                // Exact executable path plus a fresh OS creation identity is the boundary.
                var before = processes.Find(targetPath).Select(candidate => candidate.Identity).ToHashSet();
                if (before.Count != 0) throw new InvalidOperationException("This game is already running. Close it before starting another session.");
                var requestedUtcTicks = DateTime.UtcNow.Ticks;
                storeLauncher.RequestLaunch(storePlan);
                using var actual = await FindStoreTarget(targetPath, before, requestedUtcTicks, token).ConfigureAwait(false);
                await ObserveAsync(id, targetPath, actual, actual, null, token, storePlan.Provider == StoreProvider.Steam).ConfigureAwait(false);
                SetState(new(id, LaunchPhase.Stopped, StopMessage(id)));
                StartAfterExitHook(id, configuration.Value?.AfterExit);
                return;
            }
            using var starter = action is null ? processes.Start(profile.ExecutablePath, profile.WorkingDirectory) : processes.Start(startPath, startFolder, action.Arguments, action.RunAsAdministrator);
            IGameProcess observed = starter;
            if (!string.Equals(startPath, targetPath, StringComparison.OrdinalIgnoreCase))
                observed = await FindTarget(starter, targetPath, token).ConfigureAwait(false);
            if (observed.HasExited)
            {
                if (observed != starter) observed.Dispose();
                throw new InvalidOperationException("The game closed before it could be confirmed running. Check its executable and Launch settings.");
            }
            await ObserveAsync(id, targetPath, starter, observed, null, token).ConfigureAwait(false);
            SetState(new(id, LaunchPhase.Stopped, StopMessage(id)));
            StartAfterExitHook(id, configuration.Value?.AfterExit);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        { SetState(new(id, LaunchPhase.Stopped, "Observation stopped. Any running game was left open.")); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or Win32Exception or NotSupportedException)
        { SetState(new(id, LaunchPhase.Failed, error.Message)); }
        catch (Exception error) { completion.TrySetException(error); }
        finally
        {
            lock (gate)
            {
                foreach (var path in reservedPaths.Where(pair => pair.Value == id).Select(pair => pair.Key).ToArray()) reservedPaths.Remove(path);
                active.Remove(id);
            }
            operation.Dispose();
            completion.TrySetResult();
        }
    }

    private async Task RunHookAsync(Guid id, string label, LaunchHook? hook, CancellationToken token)
    {
        if (hook is null) return;
        HookOutcome outcome;
        try { outcome = await hooks.RunAsync(label, hook, token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception error) { outcome = new(label, false, error.Message); }
        HookCompleted?.Invoke(this, new(id, outcome));
    }

    // The after-exit hook never keeps the game slot busy: it runs on its own once the session has been recorded.
    private void StartAfterExitHook(Guid id, LaunchHook? hook)
    {
        if (hook is null) return;
        _ = Task.Run(async () => { try { await RunHookAsync(id, "After exit", hook, shutdown.Token).ConfigureAwait(false); } catch (OperationCanceledException) { } });
    }

    private async Task<IGameProcess> FindStoreTarget(string path, HashSet<ProcessIdentity> before, long requestedUtcTicks, CancellationToken token)
    {
        var deadline = Stopwatch.StartNew();
        do
        {
            token.ThrowIfCancellationRequested();
            var candidates = processes.Find(path).Where(candidate => !before.Contains(candidate.Identity) && candidate.Identity.CreatedUtcTicks >= requestedUtcTicks).ToArray();
            if (candidates.Length > 1) throw new IOException("Multiple new game processes were found. Tracking was not started; games were left open. Choose a more specific target in Launch settings.");
            if (candidates.Length == 1)
            {
                var process = processes.Open(candidates[0].Identity);
                if (process is not null)
                {
                    // Open must preserve the sampled creation identity, including across PID reuse.
                    if (process.Identity == candidates[0].Identity && !process.HasExited) return process;
                    process.Dispose();
                }
            }
            await Task.Delay(pollInterval, token).ConfigureAwait(false);
        } while (deadline.Elapsed < storeStartupTimeout);
        throw new IOException("The actual game process was not found after the store launch request. Check that its store client is installed and signed in, then check Launch settings. Any client or game was left open.");
    }

    private async Task<IGameProcess> FindTarget(IGameProcess root, string path, CancellationToken token)
    {
        var deadline = Stopwatch.StartNew();
        do
        {
            token.ThrowIfCancellationRequested();
            var process = FindRelated(root, path);
            if (process is not null) return process;
            await Task.Delay(pollInterval, token).ConfigureAwait(false);
        } while (deadline.Elapsed < startupTimeout);
        throw new IOException("The actual game process was not found within 30 seconds. Check Launch settings. The launched program was left open if still running.");
    }

    private async Task ObserveAsync(Guid id, string targetPath, IGameProcess starter, IGameProcess observed, SessionJournal? recovered, CancellationToken token, bool throughSteam = false)
    {
        var exited = false;
        using var timerStop = new CancellationTokenSource();
        Task timer = Task.CompletedTask;
        try
        {
            string? warning = null;
            try
            {
                if (recovered is null)
                {
                    var sessionId = await recorder.BeginAsync(id, targetPath, observed, starter.RecoveryGroupName).ConfigureAwait(false);
                    // Steam counts what it started, so the combined total must know which sessions those were (a missing note only affects that sum).
                    try { await store.RecordSessionOriginAsync(id, sessionId, throughSteam).ConfigureAwait(false); }
                    catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
                }
                else await recorder.ResumeAsync(recovered, observed, starter.RecoveryGroupName).ConfigureAwait(false);
                NotifySession(id);
                timer = SampleLoop(id, timerStop.Token);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or OverflowException)
            { warning = "Game running, but playtime tracking could not start. " + error.Message; }
            try { await library.MarkPlayingAsync(id).ConfigureAwait(false); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            { warning = "The game is running, but its library status could not be saved. " + error.Message; }
            SetState(new(id, LaunchPhase.Running, warning ?? (recovered is null ? "Running" : "Running · session recovered; unobserved time excluded."), observed.Identity.Id));
            while (true)
            {
                await observed.WaitForExitAsync(token).ConfigureAwait(false);
                if (observed != starter) observed.Dispose();
                observed = FindRelated(starter, targetPath) ?? starter;
                if (observed == starter && starter.HasExited)
                {
                    if (recovered is not null && starter.RecoveryGroupName is null && processes.Find(targetPath).Length > 0)
                        throw new IOException("The recovered process exited, but another game process could not be verified. Recorded time was preserved; further tracking was interrupted.");
                    break;
                }
                // For a launcher still alive after its configured target stops, only target processes count.
                if (observed == starter && !processes.Find(targetPath).Any(p => p.Identity == starter.Identity)) break;
                await recorder.UpdateProcessAsync(id, observed).ConfigureAwait(false);
            }
            exited = true;
        }
        finally
        {
            var end = recorder.CaptureEnd(id);
            await timerStop.CancelAsync().ConfigureAwait(false);
            try { await timer.ConfigureAwait(false); } catch (OperationCanceledException) { }
            if (observed != starter) observed.Dispose();
            try { await recorder.EndAsync(id, exited ? SessionState.Completed : SessionState.Interrupted, end.Awake, end.Utc).ConfigureAwait(false); }
            finally { try { NotifySession(id); } finally { NotifySessionEnded(id,exited); } }
        }
    }

    private async Task SampleLoop(Guid id, CancellationToken token)
    {
        while (true)
        {
            await Task.Delay(TimeSpan.FromSeconds(1), token).ConfigureAwait(false);
            await recorder.SampleAsync(id, cancellationToken: token).ConfigureAwait(false);
            NotifySession(id);
        }
    }

    private void NotifySession(Guid id)
    { if (recorder.GetProgress(id) is SessionProgress progress) SessionUpdated?.Invoke(this, progress); }

    private string StopMessage(Guid id) => recorder.GetProgress(id)?.Warning is string warning ? "Game stopped. " + warning : "Game stopped. Playtime saved.";

    public async Task<string[]> RecoverAsync()
    {
        await recoveryGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (recoveryCompleted) return [];
            var warnings = new List<string>();
            var snapshot = await store.InitializeAsync().ConfigureAwait(false);
            foreach (var game in snapshot.Games.Where(g => g.Profile.State == DocumentState.Ready))
            {
                StoredSession[] sessions;
                try { sessions = await recorder.ReadAsync(game.Id).ConfigureAwait(false); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                { warnings.Add("Session recovery could not read a game's checkpoints. " + error.Message); continue; }
                foreach (var item in sessions.OrderByDescending(s => s.Document.Value?.ObservedAt))
                {
                    if (item.Document.IsReadOnly || item.Document.Value is not SessionJournal journal)
                    { warnings.Add(item.Document.Message ?? "A session checkpoint needs attention; it was preserved."); continue; }
                    try
                    {
                        if (journal.State == SessionState.Active) journal = journal with { State = SessionState.Interrupted };
                        await store.SaveSessionJournalAsync(journal).ConfigureAwait(false);
                        if (journal.State != SessionState.Interrupted || game.Profile.Value!.IsArchived) continue;
                        lock (gate) { if (active.ContainsKey(game.Id)) continue; }
                        var process = processes.Recover(journal.ExecutablePath, new(journal.ProcessId, journal.ProcessCreatedUtcTicks), journal.ProcessGroupName);
                        if (process is null)
                        { warnings.Add($"{game.Profile.Value.Name}: interrupted session recovered through its last saved checkpoint; unobserved time was excluded."); continue; }
                        if (!StartRecovered(journal, process, game.Profile.Value)) continue;
                        warnings.Add($"{game.Profile.Value.Name}: resumed a verified running game; time while GameVault was absent was excluded.");
                    }
                    catch (Exception error) when (error is IOException or UnauthorizedAccessException or Win32Exception or InvalidOperationException)
                    { warnings.Add("Session recovery needs attention. " + error.Message); }
                }
            }
            recoveryCompleted = true;
            return warnings.Distinct().ToArray();
        }
        finally { recoveryGate.Release(); }
    }

    private bool StartRecovered(SessionJournal journal, IGameProcess process, GameProfile profile)
    {
        var reservations = new[] { journal.ExecutablePath, profile.ExecutablePath }.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        try
        {
            // The journal supplies the already verified game target. Store metadata is
            // optional during recovery: an unavailable client must not lose a session.
            var plan = storeLauncher.Resolve(profile.ExecutablePath, journal.ExecutablePath);
            if (plan is not null) reservations = reservations.Append("store:" + plan.StoreKey).ToArray();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or Win32Exception or InvalidOperationException)
        { /* Preserve exact-process recovery when installation metadata is unavailable. */ }
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        IDisposable operation;
        lock (gate)
        {
            if (disposed || !active.TryAdd(journal.GameId, completion)) { process.Dispose(); return false; }
            if (reservations.Any(reservedPaths.ContainsKey)) { active.Remove(journal.GameId); process.Dispose(); return false; }
            var acquired=operations.TryAcquire(journal.GameId);
            if(acquired is null){active.Remove(journal.GameId);process.Dispose();return false;}
            operation=acquired;
            foreach (var path in reservations) reservedPaths.Add(path, journal.GameId);
        }
        SetState(new(journal.GameId, LaunchPhase.Starting, "Recovering running session…"));
        _ = Task.Run(async () =>
        {
            using (process)
            {
                try
                {
                    await ObserveAsync(journal.GameId, journal.ExecutablePath, process, process, journal, shutdown.Token).ConfigureAwait(false);
                    SetState(new(journal.GameId, LaunchPhase.Stopped, StopMessage(journal.GameId)));
                }
                catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
                { SetState(new(journal.GameId, LaunchPhase.Stopped, "Observation stopped. Any running game was left open.")); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or Win32Exception or InvalidOperationException)
                { SetState(new(journal.GameId, LaunchPhase.Failed, error.Message)); }
                catch (Exception error) { completion.TrySetException(error); }
                finally
                {
                    lock (gate)
                    {
                        active.Remove(journal.GameId);
                        foreach (var path in reservedPaths.Where(pair => pair.Value == journal.GameId).Select(pair => pair.Key).ToArray()) reservedPaths.Remove(path);
                    }
                    operation.Dispose();
                    completion.TrySetResult();
                }
            }
        });
        return true;
    }

    private IGameProcess? FindRelated(IGameProcess root, string path)
    {
        foreach (var candidate in processes.Find(path).Where(root.Owns))
        {
            var process = processes.Open(candidate.Identity);
            if (process is null) continue;
            if (!process.HasExited) return process;
            process.Dispose();
        }
        return null;
    }

    private static void ValidateExecutable(string path)
    {
        if (!Path.IsPathFullyQualified(path) || !string.Equals(Path.GetExtension(path), ".exe", StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
            throw new IOException("The executable was not found. Repair the game path through Edit game or Launch settings.");
    }

    private void SetState(LaunchState state)
    {
        lock (gate) states[state.GameId] = state;
        StateChanged?.Invoke(this, state);
    }

    public async ValueTask DisposeAsync()
    {
        Task[] pending;
        lock (gate) { disposed = true; pending = active.Values.Select(c => c.Task).ToArray(); }
        await shutdown.CancelAsync().ConfigureAwait(false);
        await Task.WhenAll(pending).ConfigureAwait(false);
        await recorder.FlushClosedAsync().ConfigureAwait(false);
    }
}
