using GameVault.Core;
using GameVault.Infrastructure.Library;
using GameVault.Infrastructure.Launching;
using GameVault.Infrastructure.Storage;

namespace GameVault.Tests;

public sealed class StoreLaunchTests : IAsyncLifetime
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "GameVault detached tests", Guid.NewGuid().ToString("N"));
    private ApplicationPaths paths = null!; private AppDataStore store = null!; private GameProfile profile = null!; private string target = "";
    private readonly Processes processes = new(); private readonly StoreLauncher activation = new(); private LaunchService service = null!;
    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(root); var selected = Path.Combine(root, "starter.exe"); target = Path.Combine(root, "actual.exe"); File.WriteAllText(selected, "fixture"); File.WriteAllText(target, "fixture");
        paths = ApplicationPaths.FromRoot(root); store = new(paths); profile = (await new LibraryService(paths, store).SaveAsync(new(null, "Store game", selected, root, []))).Profile;
        activation.Plan = new(StoreProvider.Steam, "Steam:10", "steam://run/10", root, target);
        service = new(paths, store, processes, pollInterval: TimeSpan.FromMilliseconds(5), storeStartupTimeout: TimeSpan.FromMilliseconds(150), storeLauncher: activation);
    }
    [Fact] public async Task ExistingClientDetachedGameIsObservedAndJournalUsesActualExecutable()
    {
        GameSessionEnded? ended = null; service.SessionEnded += (_, value) => ended = value;
        activation.OnLaunch = () => processes.Game = new(80, DateTime.UtcNow.Ticks);
        var run = service.RunAsync(profile.Id); await Running();
        Assert.Equal(0, processes.Starts); Assert.Equal(80, service.GetState(profile.Id).ProcessId);
        Assert.Equal(target, Assert.Single(await store.LoadSessionJournalsAsync(profile.Id)).Document.Value!.ExecutablePath);
        Assert.False(run.IsCompleted); processes.Game!.Exit(); await run;
        Assert.True(ended!.ConfirmedExit); Assert.Equal(SessionState.Completed, Assert.Single(await store.LoadSessionJournalsAsync(profile.Id)).Document.Value!.State);
    }
    [Fact] public async Task ASessionStartedThroughSteamIsRememberedAsOne()
    {
        activation.OnLaunch = () => processes.Game = new(80, DateTime.UtcNow.Ticks);
        var run = service.RunAsync(profile.Id); await Running(); processes.Game!.Exit(); await run;
        var origins = (await store.LoadSessionOriginsAsync(profile.Id)).Value!;
        Assert.Equal(Assert.Single(await store.LoadSessionJournalsAsync(profile.Id)).Id, Assert.Single(origins.Steam));
        Assert.Empty(origins.Other);
    }
    [Fact] public async Task TimeoutDoesNotCreateHistoryOrPromoteStatus()
    {
        await service.RunAsync(profile.Id); Assert.Equal(LaunchPhase.Failed, service.GetState(profile.Id).Phase);
        Assert.Empty(await store.LoadSessionJournalsAsync(profile.Id)); Assert.Equal(GameStatus.NotStarted, (await store.LoadProfileAsync(profile.Id)).Profile.Value!.Status);
    }
    [Fact] public async Task OldIdentityAfterActivationCannotBeAttached()
    {
        activation.OnLaunch = () => processes.Game = new(80, 1);
        await service.RunAsync(profile.Id); Assert.Equal(LaunchPhase.Failed, service.GetState(profile.Id).Phase); Assert.Empty(await store.LoadSessionJournalsAsync(profile.Id)); Assert.False(processes.Game!.HasExited);
    }
    [Fact] public async Task PreexistingActualGameBlocksActivationAndRunningProbeUsesResolvedTarget()
    {
        processes.Game = new(80, 1); Assert.True(await service.IsGameRunningAsync(profile.Id));
        await service.RunAsync(profile.Id); Assert.Equal(0, activation.Requests); Assert.Equal(LaunchPhase.Failed, service.GetState(profile.Id).Phase);
    }
    [Fact] public async Task CancellationLeavesDetachedGameOpenAndMarksInterrupted()
    {
        activation.OnLaunch = () => processes.Game = new(80, DateTime.UtcNow.Ticks); var run = service.RunAsync(profile.Id); await Running();
        await service.DisposeAsync(); await run; Assert.False(processes.Game!.HasExited);
        Assert.Equal(SessionState.Interrupted, Assert.Single(await store.LoadSessionJournalsAsync(profile.Id)).Document.Value!.State);
    }
    [Fact] public async Task MultipleNewExactTargetsAreRejectedWithoutHistory()
    {
        activation.OnLaunch = () => { processes.Game = new(80, DateTime.UtcNow.Ticks); processes.Ambiguous = true; };
        await service.RunAsync(profile.Id); Assert.Equal(LaunchPhase.Failed, service.GetState(profile.Id).Phase); Assert.Empty(await store.LoadSessionJournalsAsync(profile.Id));
    }
    [Fact] public async Task StoreIdentityBlocksDifferentProfilesWithDifferentExecutablePaths()
    {
        var otherExe = Path.Combine(root, "other.exe"); var otherTarget = Path.Combine(root, "otheractual.exe"); File.WriteAllText(otherExe, "fixture"); File.WriteAllText(otherTarget, "fixture");
        var other = (await new LibraryService(paths, store).SaveAsync(new(null, "Other", otherExe, root, []))).Profile;
        activation.Resolver = (exe, _) => activation.Plan with { TargetExecutablePath = exe == otherExe ? otherTarget : target };
        // The first launch must still be waiting for its game when the second is tried. The shared service gives up after 150 ms, which a slow build
        // machine can exceed, releasing the store identity; this one waits long enough (it is disposed as soon as the check is done).
        await using var patient = new LaunchService(paths, store, processes, pollInterval: TimeSpan.FromMilliseconds(5), storeStartupTimeout: TimeSpan.FromMinutes(1), storeLauncher: activation);
        var first = patient.RunAsync(profile.Id); await Requested(); await patient.RunAsync(other.Id);
        Assert.Equal(LaunchPhase.Failed, patient.GetState(other.Id).Phase); Assert.Equal(1, activation.Requests);
        await patient.DisposeAsync(); await first;
    }
    [Fact] public async Task CancellationDuringStoreStartupCreatesNoHistory()
    {
        var run = service.RunAsync(profile.Id); await Requested(); await service.DisposeAsync(); await run;
        Assert.Empty(await store.LoadSessionJournalsAsync(profile.Id)); Assert.Equal(1, activation.Requests); Assert.Equal(0, processes.Starts);
    }
    [Fact] public async Task ChangedIdentityWhenOpeningCandidateCannotBeAttached()
    {
        activation.OnLaunch = () => { processes.Game = new(80, DateTime.UtcNow.Ticks); processes.ReusedIdentity = true; };
        await service.RunAsync(profile.Id); Assert.Equal(LaunchPhase.Failed, service.GetState(profile.Id).Phase); Assert.Empty(await store.LoadSessionJournalsAsync(profile.Id));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RecoveryUsesRecordedActualProcessWithoutAnotherStoreActivation(bool metadataUnavailable)
    {
        activation.OnLaunch = () => processes.Game = new(80, DateTime.UtcNow.Ticks); var first = service.RunAsync(profile.Id); await Running();
        await service.DisposeAsync(); await first;
        if (metadataUnavailable) activation.Resolver = (_, _) => throw new IOException("Installation metadata unavailable");
        await using var recovered = new LaunchService(paths, store, processes, storeLauncher: activation);
        Assert.NotEmpty(await recovered.RecoverAsync());
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5)); while (recovered.GetState(profile.Id).Phase != LaunchPhase.Running) await Task.Delay(5, timeout.Token);
        Assert.Equal(1, activation.Requests); Assert.Equal(80, recovered.GetState(profile.Id).ProcessId); processes.Game!.Exit(); await recovered.WaitForIdleAsync(profile.Id);
        Assert.Equal(SessionState.Completed, Assert.Single(await store.LoadSessionJournalsAsync(profile.Id)).Document.Value!.State);
    }
    [Fact] public async Task RecoveredStoreIdentityBlocksAnotherProfileWithDifferentExecutablePaths()
    {
        activation.OnLaunch = () => processes.Game = new(80, DateTime.UtcNow.Ticks);
        var first = service.RunAsync(profile.Id); await Running();
        await service.DisposeAsync(); await first;
        var otherExe = Path.Combine(root, "other.exe"); var otherTarget = Path.Combine(root, "otheractual.exe");
        File.WriteAllText(otherExe, "fixture"); File.WriteAllText(otherTarget, "fixture");
        var other = (await new LibraryService(paths, store).SaveAsync(new(null, "Other", otherExe, root, []))).Profile;
        activation.Resolver = (exe, _) => activation.Plan with { TargetExecutablePath = exe == otherExe ? otherTarget : target };
        await using var recovered = new LaunchService(paths, store, processes, pollInterval: TimeSpan.FromMilliseconds(5), storeStartupTimeout: TimeSpan.FromMilliseconds(150), storeLauncher: activation);
        await recovered.RecoverAsync();
        await recovered.RunAsync(other.Id);
        Assert.Equal(1, activation.Requests);
        Assert.Equal(LaunchPhase.Failed, recovered.GetState(other.Id).Phase);
        processes.Game!.Exit(); await recovered.WaitForIdleAsync(profile.Id);
    }
    [Fact] public async Task ConfirmedDetachedGameExitCreatesBackupWhileClientIsUnaffected()
    {
        var saves = Path.Combine(root, "saves"); Directory.CreateDirectory(saves); File.WriteAllText(Path.Combine(saves, "slot.sav"), "Synthetic save"); await store.SaveProfileAsync(profile with { SaveSources = [saves] });
        var env = new GameVault.Infrastructure.Saves.SaveDiscoveryEnvironment(Path.Combine(root, "user"), Path.Combine(root, "docs"), Path.Combine(root, "saved"), Path.Combine(root, "roam"), Path.Combine(root, "local"), Path.Combine(root, "public"), []);
        var backups = new GameVault.Infrastructure.Saves.BackupService(paths, store, new(paths, store, env), new(), service.IsGameRunningAsync);
        await using var coordinator = new GameVault.Infrastructure.Saves.BackupCoordinator(service, backups, service.WaitForIdleAsync);
        activation.OnLaunch = () => processes.Game = new(80, DateTime.UtcNow.Ticks); var run = service.RunAsync(profile.Id); await Running();
        processes.Game!.Exit(); await run; await coordinator.FlushAsync();
        Assert.Equal(BackupPhase.Complete, (await backups.ReadAsync(profile.Id)).Phase); Assert.Equal(1, activation.Requests);
    }
    private async Task Requested() { using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5)); while (activation.Requests == 0) await Task.Delay(1, timeout.Token); }
    private async Task Running() { using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5)); while (service.GetState(profile.Id).Phase != LaunchPhase.Running) { if (service.GetState(profile.Id).Phase == LaunchPhase.Failed) throw new Exception(service.GetState(profile.Id).Message); await Task.Delay(5, timeout.Token); } }
    public async Task DisposeAsync() { await service.DisposeAsync(); Directory.Delete(root, true); }
    private sealed class StoreLauncher : IStoreGameLauncher
    {
        public StoreLaunchPlan Plan = null!; public Action? OnLaunch; public int Requests; public Func<string, string?, StoreLaunchPlan>? Resolver;
        public StoreLaunchPlan? Resolve(string executable, string? targetOverride) => Resolver?.Invoke(executable, targetOverride) ?? Plan with { TargetExecutablePath = targetOverride ?? Plan.TargetExecutablePath };
        public void RequestLaunch(StoreLaunchPlan plan) { Requests++; OnLaunch?.Invoke(); }
    }
    private sealed class Game(int id, long created) : IGameProcess
    {
        private readonly TaskCompletionSource exit = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ProcessIdentity Identity => new(id, created); public bool HasExited => exit.Task.IsCompleted;
        public Task WaitForExitAsync(CancellationToken token) => exit.Task.WaitAsync(token); public void Exit() => exit.TrySetResult(); public void Dispose() { }
    }
    private sealed class Processes : IGameProcessSystem
    {
        public Game? Game; public bool Ambiguous; public bool ReusedIdentity; public int Starts;
        public IGameProcess Start(string executable, string workingDirectory) { Starts++; throw new Exception("Store games must not be started directly"); }
        public ProcessCandidate[] Find(string executable) => Path.GetFileName(executable) != "actual.exe" || Game is null || Game.HasExited ? [] : Ambiguous ? [new(Game.Identity, [400]), new(new(81, Game.Identity.CreatedUtcTicks), [400])] : [new(Game.Identity, [400])];
        public IGameProcess? Open(ProcessIdentity identity) => Game?.Identity == identity ? ReusedIdentity ? new Game(identity.Id, identity.CreatedUtcTicks + 1) : Game : null;
    }
}
