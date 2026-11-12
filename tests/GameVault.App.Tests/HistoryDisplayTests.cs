using GameVault.App;
using GameVault.Core;
namespace GameVault.App.Tests;
public sealed class HistoryDisplayTests
{
    [Fact] public void StoredCorrectionsOverrideStaleEndedProgressButNotLiveProgress()
    {
        var id=Guid.NewGuid();var profile=new GameProfile(1,id,"Game",@"D:\game.exe",@"D:\",[],GameStatus.Playing,20,false,false);
        var item=new LibraryItemView(new(id,profile,false,false,null,3000,null));
        var ended=new SessionProgress(id,Guid.NewGuid(),3600,3600,null,false);
        item.SetStoredProgress(ended);Assert.Equal("0h 50m 00s",item.Played);
        item.SetStoredProgress(ended with{IsActive=true});Assert.Equal("1h 00m 00s",item.Played);
    }
}