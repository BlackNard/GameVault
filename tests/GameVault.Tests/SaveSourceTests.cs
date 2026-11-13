using GameVault.Core;
using GameVault.Infrastructure.Saves;
namespace GameVault.Tests;
public sealed class SaveSourceTests:IDisposable
{
    private readonly string root=Path.Combine(Path.GetTempPath(),"GameVault source tests",Guid.NewGuid().ToString("N"));
    private ApplicationPaths Paths=>ApplicationPaths.FromRoot(Path.Combine(root,"app"));
    private SaveSourcePolicy Policy=>new(Paths,[Path.Combine(root,"documents")]);
    [Fact] public void MissingEmptyAndReadableSourcesRemainDistinct()
    {
        Directory.CreateDirectory(root);var folder=Path.Combine(root,"saves");var inspector=new SaveSourceInspector(Policy);
        Assert.Equal(SaveSourceAccess.Missing,inspector.Inspect(folder).Access);
        Directory.CreateDirectory(folder);Assert.Equal(SaveSourceAccess.Empty,inspector.Inspect(folder).Access);
        File.WriteAllText(Path.Combine(folder,"slot.sav"),"saved");Assert.Equal(SaveSourceAccess.Ready,inspector.Inspect(folder).Access);
        Assert.Equal(SaveSourceAccess.Ready,inspector.Inspect(Path.Combine(folder,"slot.sav")).Access);
    }
    [Fact] public void BroadManagedTraversingAndOverlappingSourcesAreRejected()
    {
        Assert.Throws<IOException>(()=>Policy.Validate(Path.GetPathRoot(root)!));
        Assert.Throws<IOException>(()=>Policy.Validate(Path.Combine(root,"documents")));
        Assert.Throws<IOException>(()=>Policy.Validate(Paths.Profiles));
        Assert.Throws<IOException>(()=>Policy.Validate(root));
        Assert.Throws<IOException>(()=>Policy.Validate(Path.Combine(root,"saves","..","other")));
        Assert.Throws<IOException>(()=>Policy.Validate(@"\\server\share\saves"));
        Assert.Throws<IOException>(()=>Policy.ValidateSet([Path.Combine(root,"saves"),Path.Combine(root,"saves","slot.sav")]));
        Assert.Single(Policy.ValidateSet([Path.Combine(root,"saves"),Path.Combine(root,"saves")]));
    }
    [Fact] public void LockedFileAndBoundedTraversalCannotLookReady()
    {
        var folder=Path.Combine(root,"saves");Directory.CreateDirectory(Path.Combine(folder,"a","b"));var file=Path.Combine(folder,"slot.sav");File.WriteAllText(file,"saved");
        using(var locked=new FileStream(file,FileMode.Open,FileAccess.ReadWrite,FileShare.None))
            Assert.Equal(SaveSourceAccess.Unreadable,new SaveSourceInspector(Policy).Inspect(folder).Access);
        Assert.Equal(SaveSourceAccess.Limited,new SaveSourceInspector(Policy,maxDepth:1).Inspect(folder).Access);
        Assert.Equal(SaveSourceAccess.Limited,new SaveSourceInspector(Policy,maxEntries:1).Inspect(folder).Access);
    }
    [Fact] public void LinksAreRejectedWithoutFollowingThem()
    {
        var outside=Path.Combine(root,"outside");var saves=Path.Combine(root,"saves");Directory.CreateDirectory(outside);Directory.CreateDirectory(saves);
        File.WriteAllText(Path.Combine(outside,"private.sav"),"private");var link=Path.Combine(saves,"linked");
        // Junction creation does not require symbolic-link privileges on Windows.
        var start=new System.Diagnostics.ProcessStartInfo("cmd.exe"){UseShellExecute=false,CreateNoWindow=true};start.ArgumentList.Add("/c");start.ArgumentList.Add("mklink");start.ArgumentList.Add("/J");start.ArgumentList.Add(link);start.ArgumentList.Add(outside);
        using var process=System.Diagnostics.Process.Start(start)!;process.WaitForExit();Assert.Equal(0,process.ExitCode);
        try{Assert.ThrowsAny<IOException>(()=>Policy.Validate(link));Assert.Equal(SaveSourceAccess.Unsafe,new SaveSourceInspector(Policy).Inspect(saves).Access);Assert.Equal("private",File.ReadAllText(Path.Combine(outside,"private.sav")));}
        finally{Directory.Delete(link);}
    }
    [Fact] public async Task LibraryRejectsUnsafeManualSourcesBeforeCreatingAProfile()
    {
        Directory.CreateDirectory(Paths.Root);var game=Path.Combine(root,"game");Directory.CreateDirectory(game);var exe=Path.Combine(game,"game.exe");File.WriteAllText(exe,"fixture");
        var store=new GameVault.Infrastructure.Storage.AppDataStore(Paths);var library=new GameVault.Infrastructure.Library.LibraryService(Paths,store);
        await Assert.ThrowsAsync<IOException>(()=>library.SaveAsync(new(null,"Game",exe,game,[Paths.Profiles])));
        await Assert.ThrowsAsync<IOException>(()=>library.SaveAsync(new(null,"Game",exe,game,[game])));
        var saves=Path.Combine(root,"saves");await Assert.ThrowsAsync<IOException>(()=>library.SaveAsync(new(null,"Game",exe,game,[saves,Path.Combine(saves,"slot.sav")])));
        Assert.Empty((await store.InitializeAsync()).Games);
        var result=await library.SaveAsync(new(null,"Game",exe,game,[saves]));Assert.Equal(saves,Assert.Single(result.Profile.SaveSources));
    }    [Theory]
    [InlineData("NUL.sav")]
    [InlineData("COM1.dat")]
    [InlineData("saves.")]
    public void DeviceNamesAndWin32PathAliasesAreUnsafe(string leaf)
    {
        Assert.ThrowsAny<IOException>(()=>Policy.Validate(Path.Combine(root,leaf)));
    }    [Fact] public void AncestorsOfProtectedProfileRootsAreRejectedButGameDescendantsRemainAllowed()
    {
        var parent=Path.Combine(root,"people");var home=Path.Combine(parent,"player");var policy=new SaveSourcePolicy(Paths,[home]);
        Assert.ThrowsAny<IOException>(()=>policy.Validate(parent));
        var narrow=Path.Combine(home,"Saved Games","Adventure");Assert.Equal(narrow,policy.Validate(narrow));
    }    public void Dispose(){if(Directory.Exists(root))Directory.Delete(root,true);}
}