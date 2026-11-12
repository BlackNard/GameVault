using GameVault.Core;
namespace GameVault.Tests;
public sealed class DurationCorrectionDraftTests
{
    [Fact] public void CorrectionDraftRejectsFractionalOutOfRangeOverflowAndMissingReason()
    {
        var d=new DurationCorrectionDraft(3029);Assert.False(d.IsDirty);
        Assert.Throws<ArgumentException>(()=>d.Validate());d.Reason="Fix";d.Hours="1.5";Assert.Throws<ArgumentException>(()=>d.Validate());
        d.Hours="1";d.Minutes="60";Assert.Throws<ArgumentException>(()=>d.Validate());d.Minutes="0";d.Hours=long.MaxValue.ToString();Assert.Throws<ArgumentException>(()=>d.Validate());
        d.Hours="1";d.Seconds="0";Assert.Equal(3600,d.Validate());Assert.True(d.IsDirty);
    }
}