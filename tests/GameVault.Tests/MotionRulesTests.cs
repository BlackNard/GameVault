using GameVault.Core;

namespace GameVault.Tests;

/// <summary>The motion rules of ticket #55: when anything moves at all, and how briefly.</summary>
public sealed class MotionRulesTests
{
    [Theory]
    [InlineData(false, true, true)]
    [InlineData(true, true, false)]     // Reduce motion
    [InlineData(false, false, false)]   // Windows animations turned off
    [InlineData(true, false, false)]
    public void NothingMovesWhenMotionIsReducedOrWindowsAnimationsAreOff(bool reduce, bool windows, bool expected) =>
        Assert.Equal(expected, MotionRules.Enabled(reduce, windows));

    [Fact]
    public void EveryTransitionIsShortAndSmall()
    {
        Assert.All(Enum.GetValues<MotionKind>().Where(k => k != MotionKind.None), kind =>
        {
            var spec = MotionRules.For(kind);
            Assert.InRange(spec.Milliseconds, 120, 250);
            Assert.InRange(Math.Abs(spec.OffsetX) + Math.Abs(spec.OffsetY), 0, 48);
        });
        Assert.Equal(0, MotionRules.For(MotionKind.None).Milliseconds);
    }

    [Fact]
    public void ListsAreUpdatedInPlaceSoWhatStaysIsNotRecreated()
    {
        var shown = new System.Collections.ObjectModel.ObservableCollection<string> { "a", "b", "c" };
        var changes = new List<string>();
        shown.CollectionChanged += (_, e) => changes.Add(e.Action.ToString());
        ListSync.Apply(shown, ["a", "c", "d"]);
        Assert.Equal(["a", "c", "d"], shown);
        Assert.Equal(["Remove", "Add"], changes);
        changes.Clear();
        ListSync.Apply(shown, ["a", "c", "d"]);
        Assert.Empty(changes);
        ListSync.Apply(shown, ["new", "d", "a"]);
        Assert.Equal(["new", "d", "a"], shown);
        ListSync.Apply(shown, []);
        Assert.Empty(shown);
    }
}
