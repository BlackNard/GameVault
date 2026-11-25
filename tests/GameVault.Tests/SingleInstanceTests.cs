using System.Runtime.Versioning;
using GameVault.Infrastructure;

namespace GameVault.Tests;

[SupportedOSPlatform("windows")]
public sealed class SingleInstanceTests
{
    private static string Folder() => Path.Combine(Path.GetTempPath(), "GameVault single", Guid.NewGuid().ToString("N"));
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(20);

    [Fact]
    public void ASecondStartWakesTheRunningCopyEveryTime()
    {
        var folder = Folder();
        var shown = 0;
        var allowed = 0;
        using var running = SingleInstance.Listen(folder, () => { Interlocked.Increment(ref shown); return true; });
        Assert.True(SingleInstance.TryActivateExisting(folder, Wait, () => allowed++));
        Assert.True(SingleInstance.TryActivateExisting(folder + Path.DirectorySeparatorChar, Wait));      // same folder, written differently
        Assert.True(SingleInstance.TryActivateExisting(folder.ToUpperInvariant(), Wait));
        Assert.Equal(3, shown);
        Assert.Equal(1, allowed);
    }

    [Fact]
    public void NothingRunningOrAnotherFolderMeansStartNormally()
    {
        var folder = Folder();
        Assert.False(SingleInstance.TryActivateExisting(folder, Wait));
        using var running = SingleInstance.Listen(folder, () => throw new Xunit.Sdk.XunitException("Another folder must not wake this one."));
        Assert.False(SingleInstance.TryActivateExisting(Folder(), Wait));
    }

    [Fact]
    public void ACopyThatCannotShowItselfOrHasClosedIsNotReportedAsShown()
    {
        var folder = Folder();
        var running = SingleInstance.Listen(folder, () => false);
        Assert.False(SingleInstance.TryActivateExisting(folder, TimeSpan.FromMilliseconds(300)));
        running.Dispose();
        Assert.False(SingleInstance.TryActivateExisting(folder, Wait));
        running.Dispose();                                    // closing twice is harmless
    }
}
