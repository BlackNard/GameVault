using GameVault.App;
namespace GameVault.App.Tests;
public sealed class CoverGridLayoutTests
{
 [Fact] public void Large_library_realizes_only_viewport_and_buffer_rows(){var l=CoverGridLayout.Calculate(1000,900,600,0);Assert.Equal(4,l.Columns);Assert.Equal(0,l.First);Assert.Equal(12,l.End);Assert.Equal(93000,l.ExtentHeight);}
 [Fact] public void Resizing_and_scroll_clamp_keep_indices_bounded(){var l=CoverGridLayout.Calculate(1000,300,600,999999);Assert.Equal(1,l.Columns);Assert.Equal(371400,l.Offset);Assert.InRange(l.First,0,999);Assert.Equal(1000,l.End);Assert.InRange(l.End-l.First,1,4);}
 [Fact] public void Empty_library_never_generates_a_container(){var l=CoverGridLayout.Calculate(0,900,600,150);Assert.Equal(0,l.First);Assert.Equal(0,l.End);Assert.Equal(0,l.Offset);}
}
