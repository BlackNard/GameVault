using GameVault.Core;
using GameVault.Infrastructure.Diagnostics;
using GameVault.Infrastructure.Storage;

namespace GameVault.Infrastructure.Saves;

/// <summary>
/// While a game GameVault started is running, notes which files are written under the watched folders, and at its end keeps the folders that
/// look like save folders in <c>save-finds.json</c> (issue #111). No watcher exists while no game runs; no file is opened while playing.
/// </summary>
public sealed class SaveActivityWatcher : IAsyncDisposable
{
    public const int MaxRecorded = 5000;
    private readonly IGameSessionSignals signals;
    private readonly AppDataStore store;
    private readonly ISaveDiscoveryScanner scanner;
    private readonly Func<Guid, IReadOnlyList<string>> rootsFor;
    private readonly Func<bool> enabled;
    private readonly TimeSpan settle;
    private readonly object gate = new();
    private readonly Dictionary<Guid, Session> running = [];
    private readonly HashSet<Session> watching = [];                    // running, or still settling after the game closed
    private readonly Dictionary<Guid, Task> work = [];
    private readonly CancellationTokenSource quitting = new();
    private int watchers;
    private Task? drain;

    public SaveActivityWatcher(IGameSessionSignals signals, AppDataStore store, ISaveDiscoveryScanner scanner, Func<Guid, IReadOnlyList<string>> rootsFor,
        Func<bool> enabled, TimeSpan? settle = null)
    {
        this.signals = signals; this.store = store; this.scanner = scanner; this.rootsFor = rootsFor; this.enabled = enabled;
        this.settle = settle ?? TimeSpan.FromSeconds(30);
        signals.StateChanged += StateChanged;
        signals.SessionEnded += SessionEnded;
    }

    public int ActiveWatcherCount { get { lock (gate) return watchers; } }

    /// <summary>The start or the end of this game's watching that is still in progress.</summary>
    public Task FlushAsync(Guid gameId) { lock (gate) return work.TryGetValue(gameId, out var task) ? task : Task.CompletedTask; }

    private void StateChanged(object? sender, LaunchState state)
    {
        if (state.Phase == LaunchPhase.Failed) { End(state.GameId, false); return; }
        if (state.Phase != LaunchPhase.Running) return;
        lock (gate)
        {
            if (drain is not null || running.ContainsKey(state.GameId) || !enabled()) return;
            var session = new Session(state.GameId);
            running.Add(state.GameId, session);
            watching.Add(session);
            if (watching.Any(other => other.Id != session.Id)) foreach (var each in watching) each.Shared = true;
            var previous = work.TryGetValue(state.GameId, out var task) ? task : Task.CompletedTask;
            session.Started = work[state.GameId] = Task.Run(async () => { await previous.ConfigureAwait(false); await StartAsync(session).ConfigureAwait(false); });
        }
    }

    private void SessionEnded(object? sender, GameSessionEnded ended) => End(ended.GameId, ended.ConfirmedExit);

    private void End(Guid id, bool confirmed)
    {
        lock (gate)
        {
            if (drain is not null || !running.Remove(id, out var session)) return;
            session.Finishing = work[id] = Task.Run(() => FinishAsync(session, confirmed ? settle : TimeSpan.Zero));
        }
    }

    private async Task StartAsync(Session session)
    {
        try
        {
            if ((await store.LoadProfileAsync(session.Id).ConfigureAwait(false)).Profile.Value is not GameProfile profile) return;
            session.Tokens = GameSaveIdentity.Read(profile).Tokens;
            session.Roots = rootsFor(session.Id).Append(Path.GetDirectoryName(profile.ExecutablePath) ?? "").Where(r => !string.IsNullOrWhiteSpace(r) && Path.IsPathFullyQualified(r) && Directory.Exists(r))
                .Select(SaveSourcePolicy.Canonical).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            foreach (var root in session.Roots)
            {
                FileSystemWatcher? watcher = null;
                try
                {
                    watcher = new(root) { IncludeSubdirectories = true, InternalBufferSize = 64 * 1024, NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size };
                    FileSystemEventHandler written = (_, e) => session.Record(e.FullPath);
                    watcher.Created += written;
                    watcher.Changed += written;
                    watcher.Renamed += (_, e) => session.Record(e.FullPath);
                    watcher.Error += (_, _) => session.Partial = true;          // some writes were missed; what was seen is still true
                    watcher.EnableRaisingEvents = true;
                    lock (gate) { session.Watchers.Add(watcher); watchers++; }
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException) { watcher?.Dispose(); }
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            AppLog.Warn("Watching for saves could not start: " + error.GetType().Name);
        }
    }

    private async Task FinishAsync(Session session, TimeSpan wait)
    {
        try
        {
            await session.Started.ConfigureAwait(false);
            // A game may write its last save while closing.
            try { if (wait > TimeSpan.Zero) await Task.Delay(wait, quitting.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            Stop(session);
            var written = session.Take().Where(f => File.Exists(f)).ToArray();
            var found = SaveRootFinder.Lift(written, session.Roots, session.Tokens).Where(Directory.Exists).ToArray();
            if (found.Length == 0) return;
            var now = DateTimeOffset.UtcNow;
            await store.UpdateSaveFindsAsync(session.Id, document =>
            {
                var finds = document.Finds.ToDictionary(f => SaveSourcePolicy.Canonical(f.Path), StringComparer.OrdinalIgnoreCase);
                foreach (var folder in found)
                {
                    if (document.Rejected.Any(r => SaveSourcePolicy.Overlaps(r, folder))) continue;
                    var files = written.Count(f => SaveSourcePolicy.Within(f, folder));
                    finds[folder] = finds.TryGetValue(folder, out var old)
                        ? old with { Files = files, Sessions = old.Sessions + 1, LastSeen = now, Shared = old.Shared && session.Shared }
                        : new(folder, files, 1, now, session.Shared);
                }
                return document with { Finds = finds.Values.OrderByDescending(f => f.LastSeen).ThenByDescending(f => f.Sessions).Take(AppDataStore.MaxSaveFinds).ToArray() };
            }).ConfigureAwait(false);
            if (quitting.IsCancellationRequested) return;
            (scanner as SaveDiscoveryService)?.SearchAgain(session.Id);              // the session may have created folders
            await scanner.ScanAsync(session.Id, quitting.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            AppLog.Warn("The folders written while playing could not be kept: " + error.GetType().Name);
        }
        finally { Stop(session); }
    }

    private void Stop(Session session)
    {
        FileSystemWatcher[] stopping;
        lock (gate) { stopping = [.. session.Watchers]; session.Watchers.Clear(); watchers -= stopping.Length; watching.Remove(session); }
        foreach (var watcher in stopping) watcher.Dispose();
    }

    public ValueTask DisposeAsync()
    {
        lock (gate)
        {
            if (drain is not null) return new(drain);
            signals.StateChanged -= StateChanged;
            signals.SessionEnded -= SessionEnded;
            foreach (var session in running.Values) work[session.Id] = Task.Run(() => FinishAsync(session, TimeSpan.Zero));
            running.Clear();
            quitting.Cancel();
            drain = Task.WhenAll(work.Values.ToArray());
            return new(drain);
        }
    }

    private sealed class Session(Guid id)
    {
        private readonly HashSet<string> written = new(StringComparer.OrdinalIgnoreCase);
        public Guid Id { get; } = id;
        public bool Shared;
        public volatile bool Partial;
        public IReadOnlyList<string> Roots = [];
        public IReadOnlySet<string> Tokens = new HashSet<string>();
        public List<FileSystemWatcher> Watchers { get; } = [];
        public Task Started = Task.CompletedTask;
        public Task Finishing = Task.CompletedTask;

        /// <summary>Called by the watchers; only text is looked at.</summary>
        public void Record(string path)
        {
            if (SaveActivityFilter.Ignore(path, Roots, Tokens)) return;
            lock (written) { if (written.Count < MaxRecorded) written.Add(path); else Partial = true; }
        }

        public string[] Take() { lock (written) return [.. written]; }
    }
}
