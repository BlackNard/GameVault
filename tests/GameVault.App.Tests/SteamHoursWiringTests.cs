using System.IO;
using System.Runtime.CompilerServices;

namespace GameVault.App.Tests;

public sealed class SteamHoursWiringTests
{
    private static string Source(string file, [CallerFilePath] string here = "") =>
        File.ReadAllText(Path.Combine(Path.GetDirectoryName(here)!, "..", "..", "src", "GameVault.App", file));

    [Fact]
    public void TheHowLongToBeatProgressUsesTheSamePlaytimeAsTheTotalCard()
    {
        var steam = Source("MainWindow.SteamHours.cs");
        Assert.Contains("ShowTimeProgress();", steam);                        // refreshed whenever the total changes
        var details = Source("MainWindow.AboutDetails.cs");
        Assert.Contains("GameOverviewRules.Progress(ShownPlayedSeconds(", details);
    }
}
