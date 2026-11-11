using GameVault.Core;
using GameVault.Infrastructure.Library;
using GameVault.Infrastructure.Launching;
using GameVault.Infrastructure.Playtime;
using GameVault.Infrastructure.Storage;

namespace GameVault.Tests;

public sealed class SessionTests : IAsyncLifetime
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "GameVault sessions", Guid.NewGuid().ToString("N"));
    private AppDataStore store = null!;
    private SessionRecorder recorder = null!;
    private GameProfile game = null!;
    private readonly FakeClock clock = new();
    private readonly FakeProcess process = new();

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(root);
        var paths = ApplicationPaths.FromRoot(root); store = new(paths);
        var executable = Path.Combine(root, "game.exe"); File.WriteAllText(executable, "fixture");
        game = (await new LibraryService(paths, store).SaveAsync(new(null, "Game", executable, root, []))).Profile;
        recorder = new(store, clock);
    }

    [Fact]
    public async Task ConfirmedSessionPersistsElapsedTimeAndEndsOnce()
    {
        await recorder.BeginAsync(game.Id, game.ExecutablePath, process);
        clock.Advance(12); await recorder.SampleAsync(game.Id, true);
        clock.Advance(3); await recorder.EndAsync(game.Id, SessionState.Completed);
        await recorder.EndAsync(game.Id, SessionState.Completed);
        var session = Assert.Single((await store.LoadProfileAsync(game.Id)).Statistics.Value!.Sessions);
        Assert.Equal(15, session.DurationSeconds); Assert.NotNull(session.EndedAt);
        var journal = Assert.Single(await recorder.ReadAsync(game.Id)).Document.Value!;
        Assert.Equal(SessionState.Completed, journal.State);
    }

    [Fact]
    public async Task SleepAndWallClockChangesDoNotBecomePlaytime()
    {
        await recorder.BeginAsync(game.Id, game.ExecutablePath, process);
        clock.Advance(4); await recorder.SampleAsync(game.Id);
        clock.Now = clock.Now.AddHours(8); // Sleep/hibernate: wall advances, awake ticks do not.
        await recorder.SampleAsync(game.Id);
        clock.Now = clock.Now.AddDays(-2); // User adjusts Windows clock backwards.
        clock.Advance(6); await recorder.EndAsync(game.Id, SessionState.Completed);
        var session = Assert.Single((await store.LoadProfileAsync(game.Id)).Statistics.Value!.Sessions);
        Assert.Equal(10, session.DurationSeconds);
        Assert.True(session.EndedAt >= session.StartedAt);
    }

    [Fact]
    public async Task FractionalSamplesAreNotTruncatedIndividually()
    {
        await recorder.BeginAsync(game.Id, game.ExecutablePath, process);
        for (var i = 0; i < 5; i++) { clock.Advance(0.25); await recorder.SampleAsync(game.Id, true); }
        Assert.Equal(1, recorder.GetProgress(game.Id)!.SessionSeconds);
        Assert.Equal(TimeSpan.FromSeconds(1.25).Ticks, Assert.Single(await recorder.ReadAsync(game.Id)).Document.Value!.DurationTicks);
    }

    [Fact]
    public async Task CheckpointsUpdateOneSessionAndPreserveAdjustments()
    {
        await store.SaveStatisticsAsync(new(1, game.Id, [], [new(Guid.NewGuid(), 300, "Earlier playtime")]));
        await recorder.BeginAsync(game.Id, game.ExecutablePath, process);
        clock.Advance(10); await recorder.SampleAsync(game.Id, true); await recorder.SampleAsync(game.Id, true);
        var statistics = (await store.LoadProfileAsync(game.Id)).Statistics.Value!;
        Assert.Single(statistics.Sessions); Assert.Single(statistics.Adjustments);
        Assert.Equal(310, recorder.GetProgress(game.Id)!.TotalSeconds);
    }

    [Fact]
    public async Task NewRecorderDoesNotCountTimeWhileTheAppWasAbsent()
    {
        await recorder.BeginAsync(game.Id, game.ExecutablePath, process);
        clock.Advance(10); await recorder.EndAsync(game.Id, SessionState.Interrupted);
        var before = Assert.Single(await recorder.ReadAsync(game.Id)).Document.Value!;
        clock.Advance(500);
        recorder = new(store, clock);
        Assert.Equal(10 * TimeSpan.TicksPerSecond, Assert.Single(await recorder.ReadAsync(game.Id)).Document.Value!.DurationTicks);
        Assert.Equal(SessionState.Interrupted, before.State);
    }

    [Fact]
    public async Task AutomaticCheckpointOccursAfterTenAwakeSeconds()
    {
        await recorder.BeginAsync(game.Id, game.ExecutablePath, process);
        clock.Advance(9.5); await recorder.SampleAsync(game.Id);
        Assert.Equal(0, Assert.Single((await store.LoadProfileAsync(game.Id)).Statistics.Value!.Sessions).DurationSeconds);
        clock.Advance(0.5); await recorder.SampleAsync(game.Id);
        Assert.Equal(10, Assert.Single((await store.LoadProfileAsync(game.Id)).Statistics.Value!.Sessions).DurationSeconds);
    }

    [Fact]
    public async Task CrashAfterJournalCommitReconcilesStatisticsOnce()
    {
        var paths = ApplicationPaths.FromRoot(root);
        var fail = false;
        var faultyStore = new AppDataStore(paths, file => { if (fail && Path.GetFileName(file) == "statistics.json") throw new IOException("Simulated interrupted statistics commit"); });
        recorder = new(faultyStore, clock);
        await recorder.BeginAsync(game.Id, game.ExecutablePath, process);
        fail = true; clock.Advance(12); await recorder.SampleAsync(game.Id, true);
        Assert.NotNull(recorder.GetProgress(game.Id)!.Warning);
        Assert.Equal(0, Assert.Single((await store.LoadProfileAsync(game.Id)).Statistics.Value!.Sessions).DurationSeconds);
        clock.Advance(500);
        await using var recovery = new LaunchService(paths, store, new RecoveryProcesses(null), clock: clock);
        await recovery.RecoverAsync(); await recovery.RecoverAsync();
        var recorded = Assert.Single((await store.LoadProfileAsync(game.Id)).Statistics.Value!.Sessions);
        Assert.Equal(12, recorded.DurationSeconds);
        Assert.Equal(SessionState.Interrupted, Assert.Single(await recorder.ReadAsync(game.Id)).Document.Value!.State);
        Assert.Equal(LaunchPhase.Stopped, recovery.GetState(game.Id).Phase);
    }

    [Fact]
    public async Task RecoveryResumesSameSessionWithoutCountingDowntime()
    {
        await recorder.BeginAsync(game.Id, game.ExecutablePath, process);
        clock.Advance(10.75); await recorder.SampleAsync(game.Id, true);
        var id = recorder.GetProgress(game.Id)!.SessionId;
        clock.Advance(500);
        var live = new RecoveryProcess(new(42, 100));
        await using var recovery = new LaunchService(ApplicationPaths.FromRoot(root), store, new RecoveryProcesses(live), clock: clock);
        await recovery.RecoverAsync();
        await WaitUntil(() => recovery.GetState(game.Id).Phase == LaunchPhase.Running);
        Assert.Equal(10, recovery.GetSessionProgress(game.Id)!.SessionSeconds);
        clock.Advance(0.5); live.Exit();
        await WaitUntil(() => recovery.GetState(game.Id).Phase == LaunchPhase.Stopped);
        var recorded = Assert.Single((await store.LoadProfileAsync(game.Id)).Statistics.Value!.Sessions);
        Assert.Equal(id, recorded.Id); Assert.Equal(11, recorded.DurationSeconds);
        Assert.Equal(1, Assert.Single(await recorder.ReadAsync(game.Id)).Document.Value!.RecoveryCount);
        await recovery.RecoverAsync();
        Assert.Single((await store.LoadProfileAsync(game.Id)).Statistics.Value!.Sessions);
    }

    [Fact]
    public async Task ReusedProcessIdCannotResumeAnInterruptedGame()
    {
        await recorder.BeginAsync(game.Id, game.ExecutablePath, process);
        clock.Advance(10); await recorder.SampleAsync(game.Id, true); clock.Advance(300);
        var unrelated = new RecoveryProcess(new(42, 999));
        await using var recovery = new LaunchService(ApplicationPaths.FromRoot(root), store, new RecoveryProcesses(unrelated), clock: clock);
        await recovery.RecoverAsync();
        Assert.Equal(LaunchPhase.Stopped, recovery.GetState(game.Id).Phase);
        Assert.Equal(10, Assert.Single((await store.LoadProfileAsync(game.Id)).Statistics.Value!.Sessions).DurationSeconds);
        Assert.False(unrelated.HasExited);
    }

    [Fact]
    public async Task FutureJournalAndBackupOnlyJournalStayReadOnly()
    {
        await recorder.BeginAsync(game.Id, game.ExecutablePath, process);
        clock.Advance(10); await recorder.SampleAsync(game.Id, true);
        var id = recorder.GetProgress(game.Id)!.SessionId;
        var file = Path.Combine(root, "Profiles", game.Id.ToString("N"), "Sessions", id.ToString("N") + ".json");
        File.Delete(file);
        Assert.Equal(DocumentState.Recovered, Assert.Single(await recorder.ReadAsync(game.Id)).Document.State);
        await File.WriteAllTextAsync(file, "{\"schemaVersion\":99}");
        Assert.Equal(DocumentState.Unsupported, Assert.Single(await recorder.ReadAsync(game.Id)).Document.State);
        await using var recovery = new LaunchService(ApplicationPaths.FromRoot(root), store, new RecoveryProcesses(null), clock: clock);
        Assert.NotEmpty(await recovery.RecoverAsync());
        Assert.Equal("{\"schemaVersion\":99}", await File.ReadAllTextAsync(file));
        Assert.Equal(10, Assert.Single((await store.LoadProfileAsync(game.Id)).Statistics.Value!.Sessions).DurationSeconds);
    }

    [Fact]
    public async Task FinalizationWriteFailureCanBeRetriedBeforeNextSession()
    {
        var paths = ApplicationPaths.FromRoot(root); var fail = false;
        var faulty = new AppDataStore(paths, _ => { if (fail) throw new IOException("Simulated disk failure"); });
        recorder = new(faulty, clock);
        await recorder.BeginAsync(game.Id, game.ExecutablePath, process);
        clock.Advance(5); fail = true; await recorder.EndAsync(game.Id, SessionState.Completed);
        Assert.NotNull(recorder.GetProgress(game.Id)!.Warning);
        fail = false;
        await recorder.BeginAsync(game.Id, game.ExecutablePath, process);
        Assert.Equal(2, (await store.LoadProfileAsync(game.Id)).Statistics.Value!.Sessions.Length);
        Assert.Contains((await store.LoadProfileAsync(game.Id)).Statistics.Value!.Sessions, s => s.DurationSeconds == 5 && s.EndedAt is not null);
    }

    [Fact]
    public async Task TimingBeginsBeforeWaitingForStorage()
    {
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        var blocking = new AppDataStore(ApplicationPaths.FromRoot(root), file =>
        { if (Path.GetFileName(file) == "settings.json") { entered.Set(); if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException(); } });
        var write = blocking.SaveSettingsAsync(AppSettings.Default);
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
        Task begin;
        try
        {
            recorder = new(blocking, clock);
            begin = recorder.BeginAsync(game.Id, game.ExecutablePath, process);
            clock.Advance(5);
        }
        finally { release.Set(); }
        await write; await begin;
        await recorder.EndAsync(game.Id, SessionState.Completed);
        Assert.Equal(5, Assert.Single((await store.LoadProfileAsync(game.Id)).Statistics.Value!.Sessions).DurationSeconds);
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition()) await Task.Delay(5, deadline.Token);
    }

    [Fact]
    public async Task EndingDoesNotCountTimeSpentWaitingForCheckpointStorage()
    {
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        var block = false;
        var blocking = new AppDataStore(ApplicationPaths.FromRoot(root), file =>
        { if (block && Path.GetFileName(file) == "settings.json") { entered.Set(); if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException(); } });
        recorder = new(blocking, clock);
        await recorder.BeginAsync(game.Id, game.ExecutablePath, process);
        block = true;
        var write = blocking.SaveSettingsAsync(AppSettings.Default); Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
        Task checkpoint; Task end;
        try
        {
            clock.Advance(10); checkpoint = recorder.SampleAsync(game.Id, true);
            end = recorder.EndAsync(game.Id, SessionState.Completed);
            clock.Advance(5);
        }
        finally { release.Set(); }
        await write; await checkpoint; await end;
        Assert.Equal(10, Assert.Single((await store.LoadProfileAsync(game.Id)).Statistics.Value!.Sessions).DurationSeconds);
    }

    private sealed class RecoveryProcess(ProcessIdentity identity) : IGameProcess
    {
        private readonly TaskCompletionSource stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ProcessIdentity Identity => identity;
        public bool HasExited => stopped.Task.IsCompleted;
        public Task WaitForExitAsync(CancellationToken token) => stopped.Task.WaitAsync(token);
        public void Exit() => stopped.TrySetResult();
        public void Dispose() { }
    }

    private sealed class RecoveryProcesses(RecoveryProcess? process) : IGameProcessSystem
    {
        public IGameProcess Start(string executable, string workingDirectory) => throw new Exception("Recovery must never launch a game.");
        public ProcessCandidate[] Find(string executable) => process is { HasExited: false } ? [new(process.Identity, [])] : [];
        public IGameProcess? Open(ProcessIdentity identity) => process is { HasExited: false } && process.Identity == identity ? process : null;
    }

    public Task DisposeAsync() { Directory.Delete(root, true); return Task.CompletedTask; }

    internal sealed class FakeClock : IPlaytimeClock
    {
        public long AwakeTicks { get; private set; }
        public DateTimeOffset Now = DateTimeOffset.Parse("2026-11-12T10:00:00Z");
        public DateTimeOffset UtcNow => Now;
        public void Advance(double seconds) { AwakeTicks += TimeSpan.FromSeconds(seconds).Ticks; Now = Now.AddSeconds(seconds); }
    }

    [Fact]
    public async Task QueuedSampleCannotAddTimeAfterAnotherGameBlocksStorage()
    {
        var otherExe = Path.Combine(root, "other.exe"); File.WriteAllText(otherExe, "fixture");
        var other = (await new LibraryService(ApplicationPaths.FromRoot(root), store).SaveAsync(new(null, "Other game", otherExe, root, []))).Profile;
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        var block = false;
        var blocking = new AppDataStore(ApplicationPaths.FromRoot(root), file =>
        { if (block && Path.GetFileName(file) == "settings.json") { entered.Set(); if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException(); } });
        recorder = new(blocking, clock);
        await recorder.BeginAsync(game.Id, game.ExecutablePath, process);
        await recorder.BeginAsync(other.Id, other.ExecutablePath, process);
        block = true; var write = blocking.SaveSettingsAsync(AppSettings.Default);
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
        Task first; Task queued; Task end;
        try
        {
            clock.Advance(10);
            first = recorder.SampleAsync(game.Id, true);
            queued = recorder.SampleAsync(other.Id, true);
            end = recorder.EndAsync(other.Id, SessionState.Completed);
            clock.Advance(5);
        }
        finally { release.Set(); }
        await write; await first; await queued; await end;
        Assert.Equal(10, Assert.Single((await store.LoadProfileAsync(other.Id)).Statistics.Value!.Sessions).DurationSeconds);
    }
    private sealed class FakeProcess : IGameProcess
    {
        public ProcessIdentity Identity => new(42, 100);
        public bool HasExited => false;
        public Task WaitForExitAsync(CancellationToken token) => Task.CompletedTask;
        public void Dispose() { }
    }
}
