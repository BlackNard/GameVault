using System.ComponentModel;
using GameVault.Core;
using GameVault.Infrastructure.Library;
using GameVault.Infrastructure.Launching;
using GameVault.Infrastructure.Storage;

namespace GameVault.Tests;

/// <summary>Stop (issue #113): ask the game to close, force it only when told to, and never touch store clients. Fake processes only.</summary>
public sealed class StopGameTests : IAsyncLifetime
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "GameVault stop tests", Guid.NewGuid().ToString("N"));
    private AppDataStore store = null!; private GameProfile profile = null!; private LaunchService service = null!;
    private readonly Fake processes = new();

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(root);
        var exe = Path.Combine(root, "game.exe"); File.WriteAllText(exe, "fixture");
        var paths = ApplicationPaths.FromRoot(root); store = new(paths);
        profile = (await new LibraryService(paths, store).SaveAsync(new(null, "Stoppable", exe, root, []))).Profile;
        service = new(paths, store, processes, pollInterval: TimeSpan.FromMilliseconds(5), stopWait: TimeSpan.FromMilliseconds(200));
    }

    public async Task DisposeAsync() { await service.DisposeAsync(); try { Directory.Delete(root, true); } catch (IOException) { } }

    private async Task<Task> RunningAsync()
    {
        var run = service.RunAsync(profile.Id);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (service.GetState(profile.Id).Phase != LaunchPhase.Running) await Task.Delay(5, timeout.Token);
        return run;
    }

    [Fact]
    public async Task AGameThatClosesWhenAskedStopsAndItsSessionIsConfirmedEnded()
    {
        GameSessionEnded? ended = null; service.SessionEnded += (_, value) => ended = value;
        processes.OnAsk = identity => processes.Exit(identity);
        var run = await RunningAsync();
        var result = await service.StopAsync(profile.Id, force: false);
        await run;
        Assert.Equal(StopOutcome.Closed, result.Outcome);
        Assert.Equal([processes.Game.Identity], processes.Asked);
        Assert.Empty(processes.Ended);
        Assert.True(ended!.ConfirmedExit);                       // so the usual save backup follows
    }

    [Fact]
    public async Task AGameThatStaysOpenIsLeftRunningUntilForceIsChosen()
    {
        var run = await RunningAsync();
        var asked = await service.StopAsync(profile.Id, force: false);
        Assert.Equal(StopOutcome.StillRunning, asked.Outcome);
        Assert.Empty(processes.Ended);
        Assert.False(processes.Game.HasExited);
        var forced = await service.StopAsync(profile.Id, force: true);
        await run;
        Assert.Equal(StopOutcome.Closed, forced.Outcome);
        Assert.Equal([processes.Game.Identity], processes.Ended);
    }

    [Fact]
    public async Task AGameWithNoWindowToAskIsReportedAtOnce()
    {
        processes.HasWindow = false;
        var run = await RunningAsync();
        Assert.Equal(StopOutcome.StillRunning, (await service.StopAsync(profile.Id, force: false)).Outcome);
        processes.Exit(processes.Game.Identity); await run;
    }

    [Fact]
    public async Task HelpersTheGameStartedAreClosedButStoreClientsBrowsersAndStrangersAreNot()
    {
        var run = await RunningAsync();
        var helper = processes.Add(61, "helper.exe", child: true);
        foreach (var (id, name) in new[] { (62, "steam.exe"), (63, "steamwebhelper.exe"), (64, "EpicGamesLauncher.exe"), (65, "GalaxyClient.exe"), (66, "chrome.exe"), (67, "explorer.exe") })
            processes.Add(id, name, child: true);
        processes.Add(90, "unrelated.exe", child: false);
        var result = await service.StopAsync(profile.Id, force: true);
        await run;
        Assert.Equal(StopOutcome.Closed, result.Outcome);
        Assert.Equal([processes.Game.Identity.Id, helper.Id], processes.Ended.Select(p => p.Id).Order());
    }

    [Fact]
    public async Task AnAdministratorGameAsksWindowsForPermission()
    {
        processes.Denied = true;
        processes.OnElevated = (force, targets) => { Assert.False(force); foreach (var target in targets) processes.Exit(target); };
        var run = await RunningAsync();
        var result = await service.StopAsync(profile.Id, force: false);
        await run;
        Assert.Equal(StopOutcome.Closed, result.Outcome);
        Assert.Equal(1, processes.Elevations);
    }

    [Fact]
    public async Task DecliningThePermissionLeavesTheGameOpen()
    {
        processes.Denied = true;
        processes.OnElevated = (_, _) => throw new Win32Exception(1223);
        var run = await RunningAsync();
        var result = await service.StopAsync(profile.Id, force: true);
        Assert.Equal(StopOutcome.Cancelled, result.Outcome);
        Assert.False(processes.Game.HasExited);
        processes.Exit(processes.Game.Identity); await run;
    }

    [Fact]
    public async Task AGameThatSurvivesForceIsReported()
    {
        processes.Unkillable = true;
        var run = await RunningAsync();
        Assert.Equal(StopOutcome.Failed, (await service.StopAsync(profile.Id, force: true)).Outcome);
        processes.Exit(processes.Game.Identity); await run;
    }

    [Fact]
    public async Task StoppingAGameThatIsNotRunningDoesNothing()
    {
        Assert.Equal(StopOutcome.NotRunning, (await service.StopAsync(profile.Id, force: true)).Outcome);
        Assert.Empty(processes.Asked); Assert.Empty(processes.Ended);
    }

    [Fact]
    public async Task AWindowsErrorWhileStoppingIsReportedWithoutEndingTracking()
    {
        processes.Failure = new Win32Exception(31, "synthetic");
        var run = await RunningAsync();
        var result = await service.StopAsync(profile.Id, force: true);
        Assert.Equal(StopOutcome.Failed, result.Outcome);
        Assert.Equal(LaunchPhase.Running, service.GetState(profile.Id).Phase);
        processes.Exit(processes.Game.Identity); await run;
    }

    private sealed class Process(ProcessIdentity identity) : IGameProcess
    {
        private readonly TaskCompletionSource exit = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ProcessIdentity Identity => identity; public bool HasExited => exit.Task.IsCompleted;
        public Task WaitForExitAsync(CancellationToken token) => exit.Task.WaitAsync(token); public void Exit() => exit.TrySetResult(); public void Dispose() { }
    }

    private sealed class Fake : IGameProcessSystem, IGameProcessControl
    {
        private readonly List<(RunningProcess Process, bool Child)> others = [];
        public Process Game { get; private set; } = new(new(60, 1000));
        public bool HasWindow = true, Denied, Unkillable; public Exception? Failure; public int Elevations;
        public Action<ProcessIdentity>? OnAsk; public Action<bool, ProcessIdentity[]>? OnElevated;
        public readonly List<ProcessIdentity> Asked = [], Ended = [];
        public IGameProcess Start(string executable, string workingDirectory) => Game;
        public ProcessCandidate[] Find(string executable) => [];
        public IGameProcess? Open(ProcessIdentity identity) => null;
        public ProcessIdentity Add(int id, string name, bool child)
        {
            var identity = new ProcessIdentity(id, 2000 + id);
            others.Add((new(new(identity, child ? [Game.Identity.Id] : [1]), name), child));
            return identity;
        }
        public void Exit(ProcessIdentity identity)
        {
            others.RemoveAll(o => o.Process.Candidate.Identity == identity);
            if (identity == Game.Identity) Game.Exit();
        }
        public RunningProcess[] Running() =>
            [.. Game.HasExited ? Array.Empty<RunningProcess>() : [new(new(Game.Identity, [1]), "game.exe")], .. others.Select(o => o.Process)];
        public bool RequestClose(ProcessIdentity identity)
        {
            if (Failure is not null) throw Failure;
            if (Denied) throw new Win32Exception(5);
            if (!HasWindow) return false;
            Asked.Add(identity); OnAsk?.Invoke(identity); return true;
        }
        public void End(ProcessIdentity identity)
        {
            if (Failure is not null) throw Failure;
            if (Denied) throw new Win32Exception(5);
            Ended.Add(identity); if (!Unkillable) Exit(identity);
        }
        public Task EndElevatedAsync(bool force, ProcessIdentity[] processes, CancellationToken token) { Elevations++; OnElevated?.Invoke(force, processes); return Task.CompletedTask; }
    }
}
