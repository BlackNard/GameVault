using System.Diagnostics;
using System.IO;
using GameVault.Core;
using GameVault.Infrastructure.Library;
using GameVault.Infrastructure.Launching;
using GameVault.Infrastructure.Storage;

namespace GameVault.App.Tests;

public sealed class ProcessIntegrationTests : IAsyncLifetime
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "GameVault native fixtures", Guid.NewGuid().ToString("N"));
    private string starter = "";
    private string target = "";
    private string working = "";
    private LaunchService service = null!;
    private AppDataStore store = null!;
    private GameProfile profile = null!;
    private readonly WindowsGameProcessSystem processes = new();
    private ApplicationPaths paths = null!;

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(root);
        starter = CopyFixture("Starter with spaces"); target = CopyFixture("Actual game with spaces");
        working = Path.Combine(root, "Custom working folder"); Directory.CreateDirectory(working);
        paths = ApplicationPaths.FromRoot(root); store = new(paths);
        profile = (await new LibraryService(paths, store).SaveAsync(new(null, "Fixture", starter, working, []))).Profile;
        service = new(paths, store, processes, TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(25));
    }

    [Fact]
    public async Task DirectFixtureUsesWorkingFolderAndObservedExit()
    {
        var run = service.RunAsync(profile.Id);
        await WaitUntil(() => service.GetState(profile.Id).Phase == LaunchPhase.Running);
        await WaitUntil(() => File.Exists(Path.Combine(working, "ready.txt")));
        Assert.True(int.TryParse(await File.ReadAllTextAsync(Path.Combine(working, "ready.txt")), out var id));
        Assert.Equal(id, service.GetState(profile.Id).ProcessId);
        Assert.Single(processes.Find(starter));
        await File.WriteAllTextAsync(Path.Combine(working, "exit.txt"), "exit");
        await run.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(LaunchPhase.Stopped, service.GetState(profile.Id).Phase);
    }

    [Fact]
    public async Task ChildKeepsRunningAfterLauncherExits()
    {
        await File.WriteAllTextAsync(Path.Combine(working, "child.txt"), target);
        await store.SaveLaunchSettingsAsync(new(1, profile.Id, target));
        var run = service.RunAsync(profile.Id);
        await WaitUntil(() => service.GetState(profile.Id).Phase == LaunchPhase.Running);
        await WaitUntil(() => processes.Find(starter).Length == 0);
        Assert.False(run.IsCompleted);
        Assert.Single(processes.Find(target));
        await File.WriteAllTextAsync(Path.Combine(Path.GetDirectoryName(target)!, "exit.txt"), "exit");
        await run.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(LaunchPhase.Stopped, service.GetState(profile.Id).Phase);
    }

    [Fact]
    public async Task DisposingObservationDoesNotTerminateFixture()
    {
        var run = service.RunAsync(profile.Id);
        await WaitUntil(() => service.GetState(profile.Id).Phase == LaunchPhase.Running);
        var id = service.GetState(profile.Id).ProcessId!.Value;
        await service.DisposeAsync(); await run;
        using var game = Process.GetProcessById(id);
        Assert.False(game.HasExited);
        await File.WriteAllTextAsync(Path.Combine(working, "exit.txt"), "exit");
        await game.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task SameImageDescendantSurvivesItsConfiguredTargetParent()
    {
        var targetFolder = Path.GetDirectoryName(target)!;
        var grandchildWorking = Path.Combine(root, "Grandchild working"); Directory.CreateDirectory(grandchildWorking);
        await File.WriteAllTextAsync(Path.Combine(working, "child.txt"), target);
        await File.WriteAllTextAsync(Path.Combine(targetFolder, "handoff.txt"), target);
        await File.WriteAllTextAsync(Path.Combine(targetFolder, "handoff-working.txt"), grandchildWorking);
        await store.SaveLaunchSettingsAsync(new(1, profile.Id, target));
        var run = service.RunAsync(profile.Id);
        try
        {
            await WaitUntil(() => service.GetState(profile.Id).Phase == LaunchPhase.Running);
            await WaitUntil(() => File.Exists(Path.Combine(grandchildWorking, "ready.txt")));
            await Task.Delay(700);
            Assert.False(run.IsCompleted);
            Assert.Equal(LaunchPhase.Running, service.GetState(profile.Id).Phase);
        }
        finally
        {
            await File.WriteAllTextAsync(Path.Combine(grandchildWorking, "exit.txt"), "exit");
            await run.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task ShortLivedIntermediateCanExitBeforeTargetDiscovery()
    {
        var helper = CopyFixture("Short lived helper");
        await File.WriteAllTextAsync(Path.Combine(working, "child.txt"), helper);
        await File.WriteAllTextAsync(Path.Combine(Path.GetDirectoryName(helper)!, "child.txt"), target);
        await store.SaveLaunchSettingsAsync(new(1, profile.Id, target));
        var run = service.RunAsync(profile.Id);
        await WaitUntil(() => service.GetState(profile.Id).Phase == LaunchPhase.Running);
        await WaitUntil(() => processes.Find(helper).Length == 0);
        Assert.False(run.IsCompleted);
        Assert.Single(processes.Find(target));
        await File.WriteAllTextAsync(Path.Combine(Path.GetDirectoryName(target)!, "exit.txt"), "exit");
        await run.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task HelperTransfersObservationAndExitsWithoutStoppingGame()
    {
        var helper = CopyFixture("One shot launch helper");
        var system = BrokerTestSystem(helper);
        await using var launch = new LaunchService(paths, store, system);
        var run = launch.RunAsync(profile.Id);
        await WaitUntil(() => launch.GetState(profile.Id).Phase == LaunchPhase.Running);
        await WaitUntil(() => processes.Find(helper).Length == 0);
        var id = launch.GetState(profile.Id).ProcessId!.Value;
        Assert.Single(processes.Find(starter));
        await launch.DisposeAsync(); await run;
        using var game = Process.GetProcessById(id);
        Assert.False(game.HasExited);
        await File.WriteAllTextAsync(Path.Combine(working, "exit.txt"), "exit");
        await game.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task TransferredProcessGroupStillTracksChildAfterHelperExits()
    {
        var helper = CopyFixture("One shot child helper");
        await File.WriteAllTextAsync(Path.Combine(working, "child.txt"), target);
        await store.SaveLaunchSettingsAsync(new(1, profile.Id, target));
        await using var launch = new LaunchService(paths, store, BrokerTestSystem(helper));
        var run = launch.RunAsync(profile.Id);
        await WaitUntil(() => launch.GetState(profile.Id).Phase == LaunchPhase.Running);
        await WaitUntil(() => processes.Find(helper).Length == 0 && processes.Find(starter).Length == 0);
        Assert.False(run.IsCompleted);
        Assert.Single(processes.Find(target));
        await File.WriteAllTextAsync(Path.Combine(Path.GetDirectoryName(target)!, "exit.txt"), "exit");
        await run.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(LaunchPhase.Stopped, launch.GetState(profile.Id).Phase);
    }

    [Fact]
    public async Task HelperReturnsTheActualNativeLaunchError()
    {
        var helper = CopyFixture("Invalid executable helper");
        var invalidExe = Path.Combine(root, "invalid.exe");
        await File.WriteAllTextAsync(invalidExe, "This is not a Windows executable.");
        var direct = Assert.Throws<System.ComponentModel.Win32Exception>(() => WindowsProcessGroup.Start(invalidExe, working));
        var error = Assert.Throws<System.ComponentModel.Win32Exception>(() => BrokerTestSystem(helper).Start(invalidExe, working));
        Assert.Equal(direct.NativeErrorCode, error.NativeErrorCode);
        Assert.Contains($"Windows error {direct.NativeErrorCode}", error.Message);
        await WaitUntil(() => processes.Find(helper).Length == 0);
    }

    [Fact]
    public async Task UnexpectedHelperPeerIsRejectedBeforeSendingLaunchRequest()
    {
        var helper = CopyFixture("Unexpected helper peer");
        Process? actual = null;
        try
        {
            var error = Assert.Throws<IOException>(() => ElevationBroker.LaunchWithHelper(starter, working, (pipe, parent) =>
            {
                var start = new ProcessStartInfo(helper) { UseShellExecute = false, CreateNoWindow = true };
                start.ArgumentList.Add("--test-launch-helper"); start.ArgumentList.Add(pipe); start.ArgumentList.Add(parent.ToString());
                actual = Process.Start(start) ?? throw new IOException("Fixture helper did not start.");
                return Process.GetCurrentProcess(); // Deliberately report the wrong expected helper identity.
            }));
            Assert.Contains("identity", error.Message);
            Assert.Empty(processes.Find(starter));
        }
        finally
        {
            if (actual is not null) { await actual.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)); actual.Dispose(); }
        }
    }

    private static WindowsGameProcessSystem BrokerTestSystem(string helper) => new(
        (_, _) => throw new System.ComponentModel.Win32Exception(740),
        (path, working) => ElevationBroker.LaunchWithHelper(path, working, (pipe, parent) =>
        {
            var start = new ProcessStartInfo(helper) { UseShellExecute = false, CreateNoWindow = true };
            start.ArgumentList.Add("--test-launch-helper"); start.ArgumentList.Add(pipe); start.ArgumentList.Add(parent.ToString());
            return Process.Start(start) ?? throw new IOException("Fixture helper did not start.");
        }));

    private string CopyFixture(string name)
    {
        var folder = Path.Combine(root, name); Directory.CreateDirectory(folder);
        foreach (var file in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "ProcessFixture")))
            File.Copy(file, Path.Combine(folder, Path.GetFileName(file)));
        return Path.Combine(folder, "GameProcessFixture.exe");
    }

    private async Task WaitUntil(Func<bool> condition)
    {
        var timeout = Stopwatch.StartNew();
        while (!condition())
        {
            if (timeout.Elapsed > TimeSpan.FromSeconds(8)) throw new TimeoutException(service.GetState(profile.Id).Message);
            await Task.Delay(20);
        }
    }

    public async Task DisposeAsync()
    {
        await service.DisposeAsync();
        foreach (var folder in new[] { working, Path.GetDirectoryName(starter)!, Path.GetDirectoryName(target)! })
            if (Directory.Exists(folder)) await File.WriteAllTextAsync(Path.Combine(folder, "exit.txt"), "cleanup");
        // Wait only for this fixture's exact copied paths; no user applications are controlled.
        foreach (var path in new[] { starter, target })
            foreach (var candidate in processes.Find(path))
                using (var process = processes.Open(candidate.Identity))
                    if (process is not null) await process.WaitForExitAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(16));
        Directory.Delete(root, true);
    }
}
