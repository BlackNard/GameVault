using GameVault.Core;
using GameVault.Infrastructure.Storage;
using GameVault.Infrastructure.Library;
using System.ComponentModel;
using System.Diagnostics;

namespace GameVault.Infrastructure.Launching;

public sealed class LaunchService : IAsyncDisposable
{
    private readonly AppDataStore store;
    private readonly LaunchSettingsStore settings;
    private readonly LibraryService library;
    private readonly IGameProcessSystem processes;
    private readonly TimeSpan startupTimeout;
    private readonly TimeSpan pollInterval;
    private readonly object gate = new();
    private readonly CancellationTokenSource shutdown = new();
    private readonly Dictionary<Guid, TaskCompletionSource> active = [];
    private readonly Dictionary<Guid, LaunchState> states = [];
    private readonly Dictionary<string, Guid> reservedPaths = new(StringComparer.OrdinalIgnoreCase);
    private bool disposed;

    public LaunchService(ApplicationPaths paths, AppDataStore store, IGameProcessSystem processes,
        TimeSpan? startupTimeout = null, TimeSpan? pollInterval = null)
    {
        this.store = store; this.processes = processes;
        settings = new(paths, store); library = new(paths, store);
        this.startupTimeout = startupTimeout ?? TimeSpan.FromSeconds(30);
        this.pollInterval = pollInterval ?? TimeSpan.FromMilliseconds(250);
    }

    public event EventHandler<LaunchState>? StateChanged;
    public LaunchState GetState(Guid id) { lock (gate) return states.GetValueOrDefault(id) ?? new(id, LaunchPhase.Stopped, "Not running"); }

    public Task RunAsync(Guid id)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (!active.TryAdd(id, completion)) throw new InvalidOperationException("This game is already starting or running.");
        }
        SetState(new(id, LaunchPhase.Starting, "Starting game…"));
        _ = Task.Run(() => RunCore(id, completion));
        return completion.Task;
    }

    private async Task RunCore(Guid id, TaskCompletionSource completion)
    {
        var token = shutdown.Token;
        try
        {
            var game = await store.LoadProfileAsync(id).ConfigureAwait(false);
            if (game.Profile.IsReadOnly || game.Profile.Value is not GameProfile profile || profile.IsArchived)
                throw new IOException("This game profile is missing, removed, or read-only. It cannot be launched safely.");
            ValidateExecutable(profile.ExecutablePath);
            if (!Directory.Exists(profile.WorkingDirectory)) throw new IOException("The working directory is missing. Repair it through Edit game.");
            var configuration = await settings.LoadAsync(id).ConfigureAwait(false);
            if (configuration.IsReadOnly) throw new IOException(configuration.Message);
            var targetPath = configuration.Value?.TargetExecutablePath ?? profile.ExecutablePath;
            ValidateExecutable(targetPath);
            var paths = new[] { Path.GetFullPath(profile.ExecutablePath), Path.GetFullPath(targetPath) }.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            lock (gate)
            {
                if (paths.Any(path => reservedPaths.ContainsKey(path))) throw new InvalidOperationException("This executable is already starting or running for another game.");
                foreach (var path in paths) reservedPaths.Add(path, id);
            }
            foreach (var path in paths)
                if (processes.Find(path).Length > 0) throw new InvalidOperationException("This game or its launcher is already running. Close it before starting another session.");
            token.ThrowIfCancellationRequested();
            using var starter = processes.Start(profile.ExecutablePath, profile.WorkingDirectory);
            IGameProcess observed = starter;
            if (!string.Equals(profile.ExecutablePath, targetPath, StringComparison.OrdinalIgnoreCase))
                observed = await FindTarget(starter, targetPath, token).ConfigureAwait(false);
            if (observed.HasExited)
            {
                if (observed != starter) observed.Dispose();
                throw new InvalidOperationException("The game closed before it could be confirmed running. Check its executable and Launch settings.");
            }
            try
            {
                string? warning = null;
                try { await library.MarkPlayingAsync(id).ConfigureAwait(false); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                { warning = "The game is running, but its library status could not be saved. " + error.Message; }
                SetState(new(id, LaunchPhase.Running, warning ?? "Running", observed.Identity.Id));
                while (true)
                {
                    await observed.WaitForExitAsync(token).ConfigureAwait(false);
                    if (observed != starter) observed.Dispose();
                    // A configured image may have several descendant processes. Wait for the remaining ones too.
                    observed = FindRelated(starter, targetPath) ?? starter;
                    if (observed == starter && starter.HasExited) break;
                    if (observed == starter && !string.Equals(profile.ExecutablePath, targetPath, StringComparison.OrdinalIgnoreCase)) break;
                }
            }
            finally { if (observed != starter) observed.Dispose(); }
            SetState(new(id, LaunchPhase.Stopped, "Game stopped."));
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
            completion.TrySetResult();
        }
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
    }
}
