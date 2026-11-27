using System.Diagnostics;
using System.Runtime.Versioning;
using GameVault.Infrastructure.Launching;

namespace GameVault.Tests;

/// <summary>The Windows side of Stop against the windowless, test-owned fixture program only.</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsStopTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "GameVault stop windows", Guid.NewGuid().ToString("N"));
    private readonly List<IGameProcess> started = [];
    private readonly WindowsGameProcessSystem system = new();

    private static string Fixture
    {
        get
        {
            var here = new DirectoryInfo(AppContext.BaseDirectory);
            var configuration = here.Parent!.Name;
            while (here is not null && !File.Exists(Path.Combine(here.FullName, "GameVault.slnx"))) here = here.Parent;
            return Directory.EnumerateFiles(Path.Combine(here!.FullName, "tests", "Fixtures", "GameProcessFixture", "bin", configuration), "GameProcessFixture.exe", SearchOption.AllDirectories).First();
        }
    }

    private IGameProcess Start()
    {
        Directory.CreateDirectory(root);
        var folder = Directory.CreateDirectory(Path.Combine(root, Guid.NewGuid().ToString("N"))).FullName;
        var process = system.Start(Fixture, folder);
        started.Add(process);
        return process;
    }

    public void Dispose()
    {
        foreach (var process in started) { system.End(process.Identity); process.Dispose(); }          // only what this test started
        try { Directory.Delete(root, true); } catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }

    private static async Task ExitedAsync(IGameProcess process)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await process.WaitForExitAsync(timeout.Token);
    }

    [Fact]
    public void ARunningProgramIsListedWithItsNameAndStarters()
    {
        var game = Start();
        var listed = Assert.Single(system.Running(), p => p.Candidate.Identity == game.Identity);
        Assert.Equal("GameProcessFixture.exe", listed.Name);
        Assert.Contains(Environment.ProcessId, listed.Candidate.Ancestors);
    }

    [Fact]
    public void AProgramWithoutAWindowCannotBeAsked()
    {
        var game = Start();
        Assert.False(system.RequestClose(game.Identity));
        Assert.False(game.HasExited);
    }

    [Fact]
    public async Task EndStopsExactlyThatProcess()
    {
        var first = Start();
        var second = Start();
        system.End(new(first.Identity.Id, first.Identity.CreatedUtcTicks + 1));       // a reused number is never ended
        Assert.False(first.HasExited);
        system.End(first.Identity);
        await ExitedAsync(first);
        Assert.False(second.HasExited);
        system.End(first.Identity);                                                    // already gone: nothing happens
    }

    [Fact]
    public async Task TheHelperDoesWhatItsArgumentsSay()
    {
        var game = Start();
        var args = ElevationBroker.StopArguments(true, [game.Identity]);
        Assert.Equal(["--stop-helper", "end", $"{game.Identity.Id}:{game.Identity.CreatedUtcTicks}"], args);
        Assert.Equal(0, ElevationBroker.RunStopHelper(args));
        await ExitedAsync(game);
    }

    [Theory]
    [InlineData("--stop-helper")]
    [InlineData("--stop-helper|end")]
    [InlineData("--stop-helper|kill|4:5")]
    [InlineData("--stop-helper|end|4")]
    [InlineData("--stop-helper|end|-4:5")]
    [InlineData("--stop-helper|end|4:5|x:y")]
    [InlineData("--other|end|4:5")]
    public void BadHelperArgumentsDoNothing(string joined)
    {
        Assert.Equal(1, ElevationBroker.RunStopHelper(joined.Split('|')));
    }

    [Fact]
    public void TheHelperRefusesTooManyTargets()
    {
        var many = Enumerable.Range(1, 65).Select(i => $"{i}:{i}");
        Assert.Equal(1, ElevationBroker.RunStopHelper(["--stop-helper", "close", .. many]));
    }
}
