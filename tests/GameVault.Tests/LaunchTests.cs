using GameVault.Core;
using GameVault.Infrastructure.Library;
using GameVault.Infrastructure.Launching;
using GameVault.Infrastructure.Storage;

namespace GameVault.Tests;

public sealed class LaunchTests : IAsyncLifetime
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "GameVault launch tests", Guid.NewGuid().ToString("N"));
    private readonly FakeProcesses processes = new();
    private ApplicationPaths paths = null!;
    private AppDataStore store = null!;
    private LaunchService service = null!;
    private GameProfile profile = null!;
    private string target = "";

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(root);
        paths = ApplicationPaths.FromRoot(root); store = new(paths);
        var exe = Path.Combine(root, "game.exe"); target = Path.Combine(root, "actual game.exe");
        File.WriteAllText(exe, "fixture"); File.WriteAllText(target, "fixture");
        profile = (await new LibraryService(paths, store).SaveAsync(new(null, "Game", exe, root, []))).Profile;
        service = new(paths, store, processes, TimeSpan.FromMilliseconds(200), TimeSpan.FromMilliseconds(5));
    }

    [Fact]
    public async Task DirectLaunchTransitionsAndPreservesWorkingFolder()
    {
        var states = new List<LaunchPhase>(); service.StateChanged += (_, state) => { lock (states) states.Add(state.Phase); };
        var running = service.RunAsync(profile.Id);
        await WaitUntil(() => service.GetState(profile.Id).Phase == LaunchPhase.Running);
        Assert.Equal(profile.ExecutablePath, processes.StartedExecutable);
        Assert.Equal(root, processes.StartedWorkingDirectory);
        Assert.Equal(GameStatus.Playing, (await store.LoadProfileAsync(profile.Id)).Profile.Value!.Status);
        processes.Starter.Exit(); await running;
        Assert.Equal(new[] { LaunchPhase.Starting, LaunchPhase.Running, LaunchPhase.Stopped }, states);
    }

    [Fact]
    public async Task ConcurrentClicksCannotLaunchTwice()
    {
        var first = service.RunAsync(profile.Id);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RunAsync(profile.Id));
        await WaitUntil(() => service.GetState(profile.Id).Phase == LaunchPhase.Running);
        Assert.Equal(1, processes.Starts);
        processes.Starter.Exit(); await first;
    }

    [Fact]
    public async Task MissingExecutableFailsWithoutChangingStatus()
    {
        File.Delete(profile.ExecutablePath); await service.RunAsync(profile.Id);
        Assert.Equal(LaunchPhase.Failed, service.GetState(profile.Id).Phase);
        Assert.Equal(0, processes.Starts);
        Assert.Equal(GameStatus.NotStarted, (await store.LoadProfileAsync(profile.Id)).Profile.Value!.Status);
    }

    [Fact]
    public async Task ExistingTargetIsNotLaunchedAgain()
    {
        processes.Candidates = [new(new(88, 1), [])];
        await service.RunAsync(profile.Id);
        Assert.Equal(0, processes.Starts);
        Assert.Equal(LaunchPhase.Failed, service.GetState(profile.Id).Phase);
    }

    [Fact]
    public async Task ChildTargetRemainsRunningAfterStarterExits()
    {
        await new LaunchSettingsStore(paths, store).SaveAsync(new(1, profile.Id, target));
        var running = service.RunAsync(profile.Id);
        await WaitUntil(() => processes.Starts == 1);
        processes.Starter.Exit();
        processes.Candidates = [new(processes.Child.Identity, [processes.Starter.Identity.Id])];
        await WaitUntil(() => service.GetState(profile.Id).Phase == LaunchPhase.Running);
        Assert.Equal(processes.Child.Identity.Id, service.GetState(profile.Id).ProcessId);
        Assert.False(running.IsCompleted);
        processes.Child.Exit(); await running;
        Assert.Equal(LaunchPhase.Stopped, service.GetState(profile.Id).Phase);
    }

    [Fact]
    public async Task UnrelatedOrOldTargetCannotBeAttached()
    {
        await new LaunchSettingsStore(paths, store).SaveAsync(new(1, profile.Id, target));
        var running = service.RunAsync(profile.Id);
        await WaitUntil(() => processes.Starts == 1);
        processes.Candidates = [new(new(87, long.MaxValue), [123]), new(new(88, 1), [processes.Starter.Identity.Id])];
        await running;
        Assert.Equal(LaunchPhase.Failed, service.GetState(profile.Id).Phase);
        Assert.False(processes.Starter.HasExited);
        Assert.Equal(GameStatus.NotStarted, (await store.LoadProfileAsync(profile.Id)).Profile.Value!.Status);
    }

    [Theory]
    [InlineData(GameStatus.Completed)]
    [InlineData(GameStatus.OnHold)]
    public async Task SuccessfulLaunchPreservesManualStatuses(GameStatus status)
    {
        await store.SaveProfileAsync(profile with { Status = status });
        var running = service.RunAsync(profile.Id);
        await WaitUntil(() => service.GetState(profile.Id).Phase == LaunchPhase.Running);
        Assert.Equal(status, (await store.LoadProfileAsync(profile.Id)).Profile.Value!.Status);
        processes.Starter.Exit(); await running;
    }

    [Fact]
    public async Task StopObservingLeavesGameAlive()
    {
        var running = service.RunAsync(profile.Id);
        await WaitUntil(() => service.GetState(profile.Id).Phase == LaunchPhase.Running);
        await service.DisposeAsync(); await running;
        Assert.False(processes.Starter.HasExited);
        Assert.True(processes.Starter.Disposed);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => service.RunAsync(profile.Id));
    }

    [Fact]
    public async Task LaunchSettingsRoundTripAndFutureSchemaRemainProtected()
    {
        var settings = new LaunchSettingsStore(paths, store);
        Assert.Equal(DocumentState.Missing, (await settings.LoadAsync(profile.Id)).State);
        await settings.SaveAsync(new(1, profile.Id, target));
        Assert.Equal(target, (await new LaunchSettingsStore(paths, store).LoadAsync(profile.Id)).Value!.TargetExecutablePath);
        var file = Path.Combine(paths.Profiles, profile.Id.ToString("N"), "launch.json");
        await File.WriteAllTextAsync(file, "{\"schemaVersion\":99}");
        Assert.True((await settings.LoadAsync(profile.Id)).IsReadOnly);
        await Assert.ThrowsAsync<IOException>(() => settings.SaveAsync(new(1, profile.Id, null)));
        Assert.Equal("{\"schemaVersion\":99}", await File.ReadAllTextAsync(file));
        await service.RunAsync(profile.Id);
        Assert.Equal(0, processes.Starts);
    }

    [Fact]
    public async Task MissingWorkingFolderAndArchivedProfilesCannotStart()
    {
        await store.SaveProfileAsync(profile with { WorkingDirectory = Path.Combine(root, "missing working folder") });
        await service.RunAsync(profile.Id);
        Assert.Equal(LaunchPhase.Failed, service.GetState(profile.Id).Phase);
        await store.SaveProfileAsync(profile with { IsArchived = true });
        await service.RunAsync(profile.Id);
        Assert.Equal(0, processes.Starts);
    }

    [Fact]
    public async Task ShutdownDuringTargetSearchDoesNotPromoteStatusOrKillStarter()
    {
        await new LaunchSettingsStore(paths, store).SaveAsync(new(1, profile.Id, target));
        var run = service.RunAsync(profile.Id);
        await WaitUntil(() => processes.Starts == 1);
        await service.DisposeAsync(); await run;
        Assert.False(processes.Starter.HasExited);
        Assert.True(processes.Starter.Disposed);
        Assert.Equal(GameStatus.NotStarted, (await store.LoadProfileAsync(profile.Id)).Profile.Value!.Status);
    }

    [Fact]
    public async Task ProfilesSharingAnActualTargetCannotStartConcurrently()
    {
        var otherExe = Path.Combine(root, "other launcher.exe"); File.WriteAllText(otherExe, "fixture");
        var other = (await new LibraryService(paths, store).SaveAsync(new(null, "Other launcher", otherExe, root, []))).Profile;
        await store.SaveLaunchSettingsAsync(new(1, profile.Id, target));
        await store.SaveLaunchSettingsAsync(new(1, other.Id, target));
        var first = service.RunAsync(profile.Id);
        await WaitUntil(() => processes.Starts == 1);
        await service.RunAsync(other.Id);
        Assert.Equal(LaunchPhase.Failed, service.GetState(other.Id).Phase);
        Assert.Equal(1, processes.Starts);
        await service.DisposeAsync(); await first;
    }

    [Fact]
    public async Task StatusWriteFailureDoesNotLoseTheRunningProcess()
    {
        var failing = new AppDataStore(paths, _ => throw new IOException("Simulated status write failure"));
        await using var launcher = new LaunchService(paths, failing, processes);
        var run = launcher.RunAsync(profile.Id);
        await WaitUntil(() => launcher.GetState(profile.Id).Phase == LaunchPhase.Running);
        Assert.Contains("could not be saved", launcher.GetState(profile.Id).Message);
        Assert.False(run.IsCompleted);
        Assert.Equal(GameStatus.NotStarted, (await store.LoadProfileAsync(profile.Id)).Profile.Value!.Status);
        processes.Starter.Exit(); await run;
        Assert.Equal(LaunchPhase.Stopped, launcher.GetState(profile.Id).Phase);
    }

    [Fact]
    public async Task ProcessStartFailureCanBeRetriedWithoutPromotingStatus()
    {
        processes.StartError = new System.ComponentModel.Win32Exception(193, "Invalid application");
        await service.RunAsync(profile.Id);
        Assert.Equal(LaunchPhase.Failed, service.GetState(profile.Id).Phase);
        Assert.Equal(GameStatus.NotStarted, (await store.LoadProfileAsync(profile.Id)).Profile.Value!.Status);
        processes.StartError = null;
        var retry = service.RunAsync(profile.Id);
        await WaitUntil(() => service.GetState(profile.Id).Phase == LaunchPhase.Running);
        processes.Starter.Exit(); await retry;
    }

    private static async Task WaitUntil(Func<bool> predicate)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!predicate()) await Task.Delay(5, deadline.Token);
    }

    public async Task DisposeAsync() { await service.DisposeAsync(); Directory.Delete(root, true); }

    private sealed class FakeProcess(int id, long created) : IGameProcess
    {
        private readonly TaskCompletionSource exit = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ProcessIdentity Identity { get; } = new(id, created);
        public bool HasExited => exit.Task.IsCompleted;
        public bool Disposed { get; private set; }
        public Task WaitForExitAsync(CancellationToken cancellationToken) => exit.Task.WaitAsync(cancellationToken);
        public void Exit() => exit.TrySetResult();
        public void Dispose() => Disposed = true;
    }

    private sealed class FakeProcesses : IGameProcessSystem
    {
        public FakeProcess Starter { get; } = new(40, 100);
        public FakeProcess Child { get; } = new(41, 101);
        public ProcessCandidate[] Candidates = [];
        public int Starts;
        public string? StartedExecutable;
        public string? StartedWorkingDirectory;
        public Exception? StartError;
        public IGameProcess Start(string executable, string workingDirectory)
        { if (StartError is not null) throw StartError; StartedExecutable = executable; StartedWorkingDirectory = workingDirectory; Interlocked.Increment(ref Starts); return Starter; }
        public ProcessCandidate[] Find(string executable) => Candidates;
        public IGameProcess? Open(ProcessIdentity identity) => identity == Child.Identity ? Child : null;
    }
}
