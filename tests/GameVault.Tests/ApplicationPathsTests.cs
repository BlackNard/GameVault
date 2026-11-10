using GameVault.Core;

namespace GameVault.Tests;

public sealed class ApplicationPathsTests
{
    [Fact]
    public void PathsStayUnderTheExplicitApplicationRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "GameVault fixture with spaces");
        var paths = ApplicationPaths.FromRoot(root + Path.DirectorySeparatorChar);

        Assert.Equal(Path.GetFullPath(root), paths.Root);
        Assert.Equal(Path.Combine(root, "Data"), paths.Data);
        Assert.Equal(Path.Combine(root, "Profiles"), paths.Profiles);
        Assert.False(Directory.Exists(paths.Profiles));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("Profiles")]
    [InlineData("../GameVault")]
    public void RejectsRootsThatWouldDependOnTheWorkingDirectory(string root)
    {
        Assert.Throws<ArgumentException>(() => ApplicationPaths.FromRoot(root));
    }

    [Fact]
    public void NormalizesAbsoluteParentSegments()
    {
        var root = Path.Combine(Path.GetTempPath(), "GameVault", "..", "GameVault portable");
        Assert.Equal(Path.GetFullPath(root), ApplicationPaths.FromRoot(root).Root);
    }
}
