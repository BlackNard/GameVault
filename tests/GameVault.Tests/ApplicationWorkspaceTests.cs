using GameVault.Core;
using GameVault.Infrastructure;

namespace GameVault.Tests;

public sealed class ApplicationWorkspaceTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "GameVault tests", Guid.NewGuid().ToString("N"));

    public ApplicationWorkspaceTests() => Directory.CreateDirectory(root);

    [Fact]
    public void OpeningWorkspaceDoesNotCreateFeatureDataOrLeaveWriteProbes()
    {
        var paths = ApplicationPaths.FromRoot(root);
        using var workspace = ApplicationWorkspace.Open(paths);
        Assert.False(Directory.Exists(paths.Data));
        Assert.False(Directory.Exists(paths.Profiles));
        Assert.Empty(Directory.GetFiles(root, ".gamevault-probe-*"));
    }

    [Fact]
    public void SecondWriterIsRejectedAndTheOriginalRetainsItsLease()
    {
        var paths = ApplicationPaths.FromRoot(root);
        using var first = ApplicationWorkspace.Open(paths);
        Assert.Throws<IOException>(() => ApplicationWorkspace.Open(paths));
        Assert.Throws<IOException>(() => ApplicationWorkspace.Open(paths));
    }

    [Fact]
    public void ReleasingLeaseAllowsAnotherInstance()
    {
        var paths = ApplicationPaths.FromRoot(root);
        var first = ApplicationWorkspace.Open(paths);
        first.Dispose();
        first.Dispose();
        using var reopened = ApplicationWorkspace.Open(paths);
    }

    [Fact]
    public void SeparatePortableFoldersCanRunIndependently()
    {
        var secondRoot = Path.Combine(root, "Second portable folder");
        Directory.CreateDirectory(secondRoot);
        using var first = ApplicationWorkspace.Open(ApplicationPaths.FromRoot(root));
        using var second = ApplicationWorkspace.Open(ApplicationPaths.FromRoot(secondRoot));
    }

    [Fact]
    public void MissingRootIsNotCreatedOrRedirected()
    {
        var missing = Path.Combine(root, "missing");
        Assert.Throws<DirectoryNotFoundException>(() => ApplicationWorkspace.Open(ApplicationPaths.FromRoot(missing)));
        Assert.False(Directory.Exists(missing));
    }

    [Fact]
    public void FileCannotBeUsedAsAnApplicationDirectory()
    {
        var file = Path.Combine(root, "not a directory");
        File.WriteAllText(file, "keep this file");
        Assert.Throws<DirectoryNotFoundException>(() => ApplicationWorkspace.Open(ApplicationPaths.FromRoot(file)));
        Assert.Equal("keep this file", File.ReadAllText(file));
    }

    public void Dispose() => Directory.Delete(root, recursive: true);
}
