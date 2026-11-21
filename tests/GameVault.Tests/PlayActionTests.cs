using GameVault.Core;
using GameVault.Infrastructure.Launching;
using GameVault.Infrastructure.Library;
using GameVault.Infrastructure.Storage;

namespace GameVault.Tests;

public sealed class PlayActionTests : IAsyncLifetime
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "GameVault play actions", Guid.NewGuid().ToString("N"));
    private readonly RecordingProcesses processes = new();
    private readonly RecordingHooks hooks = new();
    private ApplicationPaths paths = null!;
    private AppDataStore store = null!;
    private LaunchService service = null!;
    private GameProfile profile = null!;
    private string modManager = "";
    private string hookProgram = "";

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(root);
        paths = ApplicationPaths.FromRoot(root);
        store = new(paths);
        var exe = Path.Combine(root, "game.exe");
        modManager = Path.Combine(root, "modmanager.exe");
        hookProgram = Path.Combine(root, "hook.exe");
        foreach (var file in new[] { exe, modManager, hookProgram }) File.WriteAllText(file, "fixture");
        profile = (await new LibraryService(paths, store).SaveAsync(new(null, "Game", exe, root, []))).Profile;
        service = new(paths, store, processes, TimeSpan.FromMilliseconds(200), TimeSpan.FromMilliseconds(5), hooks: hooks);
    }

    public async Task DisposeAsync() { await service.DisposeAsync(); Directory.Delete(root, true); }

    private PlayAction Action(string name = "Modded", string? exe = null, string args = "--profile \"a b\"", bool admin = false) =>
        new(Guid.NewGuid(), name, exe ?? modManager, args, "", admin);

    private static async Task WaitUntil(Func<bool> predicate)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!predicate()) await Task.Delay(5, deadline.Token);
    }

    [Fact]
    public void Actions_are_validated()
    {
        var id = Guid.NewGuid();
        var good = new PlayAction(Guid.NewGuid(), "Modded", @"C:\Tools\mm.exe", "--x", @"C:\Tools", false);
        Assert.True(LaunchRules.IsValid(new(1, id, null, [good], good.Id), id));
        Assert.False(LaunchRules.IsValid(new(1, id, null, [good with { ExecutablePath = "mm.exe" }]), id));
        Assert.False(LaunchRules.IsValid(new(1, id, null, [good with { ExecutablePath = @"C:\Tools\mm.bat" }]), id));
        Assert.False(LaunchRules.IsValid(new(1, id, null, [good with { Name = "  " }]), id));
        Assert.False(LaunchRules.IsValid(new(1, id, null, [good with { Arguments = "a\r\nb" }]), id));
        Assert.False(LaunchRules.IsValid(new(1, id, null, [good with { Arguments = new string('a', LaunchRules.MaxArgumentLength + 1) }]), id));
        Assert.False(LaunchRules.IsValid(new(1, id, null, [good, good with { Id = Guid.NewGuid(), Name = "MODDED" }]), id));
        Assert.False(LaunchRules.IsValid(new(1, id, null, [good], Guid.NewGuid()), id));
        Assert.False(LaunchRules.IsValid(new(1, id, null, Enumerable.Range(0, 9).Select(i => good with { Id = Guid.NewGuid(), Name = "n" + i }).ToArray()), id));
        Assert.False(LaunchRules.IsValid(new(1, id, null, null, null, new(@"C:\Tools\h.exe", "", 0, true)), id));
        Assert.False(LaunchRules.IsValid(new(1, id, null, null, null, null, new(@"C:\Tools\h.exe", "", 301, true)), id));
        Assert.True(LaunchRules.IsValid(new(1, id, null, null, null, new(@"C:\Tools\h.exe", "-q", 30, false), new(@"C:\Tools\h.exe", "", 300, true)), id));
    }

    [Fact]
    public async Task Settings_round_trip_and_older_launch_files_still_load()
    {
        var action = Action();
        var saved = new LaunchSettings(1, profile.Id, null, [action], action.Id, new(hookProgram, "-a", 20, true), null);
        await new LaunchSettingsStore(paths, store).SaveAsync(saved);
        var loaded = (await store.LoadLaunchSettingsAsync(profile.Id)).Value!;
        Assert.Equal(action, loaded.PlayActions![0]);
        Assert.Equal(action.Id, loaded.DefaultActionId);
        Assert.Equal("-a", loaded.BeforeLaunch!.Arguments);
        File.WriteAllText(Path.Combine(ProfileFolders.Resolve(paths, profile.Id), "launch.json"), $"{{\"schemaVersion\":1,\"gameId\":\"{profile.Id}\",\"targetExecutablePath\":null}}");
        Assert.Null((await store.LoadLaunchSettingsAsync(profile.Id)).Value!.PlayActions);
    }

    [Fact]
    public async Task Saving_refuses_missing_programs_and_bad_defaults()
    {
        var launchStore = new LaunchSettingsStore(paths, store);
        await Assert.ThrowsAsync<ArgumentException>(() => launchStore.SaveAsync(new(1, profile.Id, null, [Action(exe: Path.Combine(root, "missing.exe"))])));
        await Assert.ThrowsAsync<ArgumentException>(() => launchStore.SaveAsync(new(1, profile.Id, null, null, null, new(Path.Combine(root, "missing.exe"), "", 10, true))));
        await Assert.ThrowsAsync<ArgumentException>(() => launchStore.SaveAsync(new(1, profile.Id, null, [Action()], Guid.NewGuid())));
    }

    [Fact]
    public async Task Play_uses_the_default_action_with_its_arguments_and_administrator_choice_and_Empty_means_the_game_itself()
    {
        var action = Action(admin: true);
        await new LaunchSettingsStore(paths, store).SaveAsync(new(1, profile.Id, null, [action], action.Id));
        var first = service.RunAsync(profile.Id);
        await WaitUntil(() => processes.Starts == 1);
        Assert.Equal((modManager, "--profile \"a b\"", true), (processes.Executable, processes.Arguments, processes.Elevated));
        await WaitUntil(() => service.GetState(profile.Id).Phase == LaunchPhase.Failed || service.GetState(profile.Id).Phase == LaunchPhase.Running);
        processes.Starter.Exit();
        await first;

        processes.Starter = new(60, 300);
        var second = service.RunAsync(profile.Id, Guid.Empty);
        await WaitUntil(() => processes.Starts == 2);
        Assert.Equal(profile.ExecutablePath, processes.Executable);
        Assert.Equal("", processes.Arguments);
        processes.Starter.Exit();
        await second;
    }

    [Fact]
    public async Task A_named_action_runs_and_an_unknown_one_fails_clearly()
    {
        var action = Action();
        await new LaunchSettingsStore(paths, store).SaveAsync(new(1, profile.Id, null, [action]));
        var run = service.RunAsync(profile.Id, action.Id);
        await WaitUntil(() => processes.Starts == 1);
        Assert.Equal(modManager, processes.Executable);
        processes.Starter.Exit();
        await run;

        await service.RunAsync(profile.Id, Guid.NewGuid());
        Assert.Equal(LaunchPhase.Failed, service.GetState(profile.Id).Phase);
        Assert.Contains("no longer exists", service.GetState(profile.Id).Message);
        Assert.Equal(1, processes.Starts);
    }

    [Fact]
    public async Task Hooks_run_around_the_game_and_never_start_unless_configured()
    {
        var results = new List<LaunchHookResult>();
        service.HookCompleted += (_, r) => { lock (results) results.Add(r); };
        var plain = service.RunAsync(profile.Id);
        await WaitUntil(() => service.GetState(profile.Id).Phase == LaunchPhase.Running);
        processes.Starter.Exit();
        await plain;
        Assert.Empty(hooks.Calls);

        await new LaunchSettingsStore(paths, store).SaveAsync(new(1, profile.Id, null, null, null, new(hookProgram, "-before", 30, true), new(hookProgram, "-after", 30, false)));
        processes.Starter = new(61, 301);
        var run = service.RunAsync(profile.Id);
        await WaitUntil(() => service.GetState(profile.Id).Phase == LaunchPhase.Running);
        Assert.Equal(new[] { "Before launch:-before" }, hooks.Calls);
        processes.Starter.Exit();
        await run;
        await WaitUntil(() => hooks.Calls.Count == 2);
        Assert.Equal("After exit:-after", hooks.Calls[1]);
        await WaitUntil(() => results.Count == 2);
    }

    [Fact]
    public async Task A_failing_or_throwing_hook_does_not_block_the_game_or_playtime()
    {
        hooks.Fail = true;
        await new LaunchSettingsStore(paths, store).SaveAsync(new(1, profile.Id, null, null, null, new(hookProgram, "", 30, true), null));
        var failures = new List<LaunchHookResult>();
        service.HookCompleted += (_, r) => { lock (failures) failures.Add(r); };
        var run = service.RunAsync(profile.Id);
        await WaitUntil(() => service.GetState(profile.Id).Phase == LaunchPhase.Running);
        Assert.Single(failures);
        Assert.False(failures[0].Outcome.Succeeded);
        processes.Starter.Exit();
        await run;
        Assert.Equal(LaunchPhase.Stopped, service.GetState(profile.Id).Phase);
        Assert.NotNull(service.GetSessionProgress(profile.Id));
    }

    [Fact]
    public async Task The_real_hook_runner_stops_a_hook_that_exceeds_its_timeout_and_reports_missing_programs()
    {
        var runner = new ProcessHookRunner();
        var missing = await runner.RunAsync("Before launch", new(Path.Combine(root, "nope.exe"), "", 5, true), CancellationToken.None);
        Assert.False(missing.Succeeded);
        var shell = Path.Combine(Environment.SystemDirectory, "cmd.exe");
        if (!File.Exists(shell)) return;
        var quick = await runner.RunAsync("After exit", new(shell, "/c exit 0", 30, true), CancellationToken.None);
        Assert.True(quick.Succeeded);
        var failing = await runner.RunAsync("After exit", new(shell, "/c exit 3", 30, true), CancellationToken.None);
        Assert.False(failing.Succeeded);
        Assert.Contains("exit code 3", failing.Message);
        var slow = await runner.RunAsync("Before launch", new(Path.Combine(Environment.SystemDirectory, "ping.exe"), "-n 20 127.0.0.1", 1, true), CancellationToken.None);
        Assert.False(slow.Succeeded);
        Assert.Contains("stopped", slow.Message);
    }

    private sealed class RecordingHooks : ILaunchHookRunner
    {
        public readonly List<string> Calls = [];
        public bool Fail;
        public Task<HookOutcome> RunAsync(string label, LaunchHook hook, CancellationToken cancellationToken)
        {
            lock (Calls) Calls.Add(label + ":" + hook.Arguments);
            if (Fail) throw new IOException("hook exploded");
            return Task.FromResult(new HookOutcome(label, true, "Finished."));
        }
    }

    private sealed class FakeProcess(int id, long created) : IGameProcess
    {
        private readonly TaskCompletionSource exit = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ProcessIdentity Identity { get; } = new(id, created);
        public bool HasExited => exit.Task.IsCompleted;
        public Task WaitForExitAsync(CancellationToken cancellationToken) => exit.Task.WaitAsync(cancellationToken);
        public void Exit() => exit.TrySetResult();
        public void Dispose() { }
    }

    private sealed class RecordingProcesses : IGameProcessSystem
    {
        public FakeProcess Starter { get; set; } = new(40, 100);
        public int Starts;
        public string? Executable;
        public string? Arguments;
        public bool Elevated;
        public IGameProcess Start(string executable, string workingDirectory) => Start(executable, workingDirectory, "", false);
        public IGameProcess Start(string executable, string workingDirectory, string arguments, bool elevated)
        { Executable = executable; Arguments = arguments; Elevated = elevated; Interlocked.Increment(ref Starts); return Starter; }
        public ProcessCandidate[] Find(string executable) => [];
        public IGameProcess? Open(ProcessIdentity identity) => null;
    }
}
