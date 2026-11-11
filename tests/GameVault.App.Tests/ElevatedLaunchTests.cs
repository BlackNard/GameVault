using System.ComponentModel;
using GameVault.Infrastructure.Launching;

namespace GameVault.App.Tests;

public sealed class ElevatedLaunchTests
{
    [Fact]
    public void NormalLaunchDoesNotRequestAdministratorApproval()
    {
        using var process = new FakeProcess();
        var system = new WindowsGameProcessSystem((_, _) => process, (_, _) => throw new Exception("Unexpected elevation"));
        Assert.Same(process, system.Start("game.exe", "working"));
    }

    [Fact]
    public void ElevationRequiredAutomaticallyRetriesWithExactPaths()
    {
        using var process = new FakeProcess();
        var elevatedCalls = 0;
        var system = new WindowsGameProcessSystem((_, _) => throw new Win32Exception(740), (path, working) =>
        {
            Assert.Equal(@"D:\Games with spaces\game.exe", path);
            Assert.Equal(@"D:\Custom working", working);
            elevatedCalls++; return process;
        });
        Assert.Same(process, system.Start(@"D:\Games with spaces\game.exe", @"D:\Custom working"));
        Assert.Equal(1, elevatedCalls);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(193)]
    public void OtherFailuresNeverRequestElevation(int code)
    {
        var system = new WindowsGameProcessSystem((_, _) => throw new Win32Exception(code), (_, _) => throw new Exception("Unexpected elevation"));
        Assert.Equal(code, Assert.Throws<Win32Exception>(() => system.Start("game.exe", "working")).NativeErrorCode);
    }

    [Fact]
    public void CancelledApprovalHasReadableFeedback()
    {
        var system = new WindowsGameProcessSystem((_, _) => throw new Win32Exception(740), (_, _) => throw new Win32Exception(1223));
        var error = Assert.Throws<Win32Exception>(() => system.Start("game.exe", "working"));
        Assert.Equal(1223, error.NativeErrorCode);
        Assert.Contains("cancelled", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class FakeProcess : IGameProcess
    {
        public ProcessIdentity Identity => new(1, 1);
        public bool HasExited => false;
        public Task WaitForExitAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public void Dispose() { }
    }
}
