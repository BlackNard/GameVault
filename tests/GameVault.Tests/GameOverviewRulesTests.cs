using GameVault.Core;

namespace GameVault.Tests;

public sealed class GameOverviewRulesTests
{
    [Fact]
    public void ReadMoreIsOfferedOnlyForLongDescriptions()
    {
        Assert.False(GameOverviewRules.NeedsReadMore(null));
        Assert.False(GameOverviewRules.NeedsReadMore("A short one."));
        Assert.True(GameOverviewRules.NeedsReadMore(new string('a', 221)));
        Assert.True(GameOverviewRules.NeedsReadMore("a\nb\nc\nd"));
    }

    [Fact]
    public void TimeToBeatReadsAsOneLine()
    {
        var line = GameOverviewRules.TimeToBeatLine(27 * 3600, 40 * 3600, 71 * 3600, 33 * 3600);
        Assert.Equal("Main story 27 h · Main + extras 40 h · Completionist 71 h", line);
        Assert.Equal("All play styles 33 h", GameOverviewRules.TimeToBeatLine(null, null, null, 33 * 3600));
        Assert.Equal("", GameOverviewRules.TimeToBeatLine(null, 0, null, null));
        Assert.Equal("30 min", GameOverviewRules.Hours(1800));
        Assert.Equal("1 min", GameOverviewRules.Hours(5));
    }

    [Fact]
    public void ProgressComparesYourTimeWithTheMainStory()
    {
        Assert.Null(GameOverviewRules.Progress(null, 3600, null));
        Assert.Null(GameOverviewRules.Progress(100, null, null));
        var tiny = GameOverviewRules.Progress(600, 100 * 3600, null)!.Value;
        Assert.Contains("under 1% of the main story", tiny.Text);
        var half = GameOverviewRules.Progress(5 * 3600, 10 * 3600, 20 * 3600)!.Value;
        Assert.Equal(0.5, half.Share);
        Assert.Contains("50% of the main story", half.Text);
        var done = GameOverviewRules.Progress(50 * 3600, null, 10 * 3600)!.Value;
        Assert.Equal(1, done.Share);
        Assert.Contains("past an average playthrough", done.Text);
    }
}
