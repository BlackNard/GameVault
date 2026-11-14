using System.IO;
namespace GameVault.App.Tests;
public sealed class NativeFixtureCleanupTests
{
 private static string Root(){var path=Path.Combine(Path.GetTempPath(),"GameVault native fixtures",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(path);return path;}
 [Fact] public async Task Transient_image_lock_is_retried_and_releases_owned_tree()
 {
  var root=Root();using var held=new FileStream(Path.Combine(root,"fixture.dll"),FileMode.CreateNew,FileAccess.ReadWrite,FileShare.Read);var retries=0;
  await NativeFixtureCleanup.DeleteAsync(root,()=>{retries++;held.Dispose();});Assert.True(retries>=1);Assert.False(Directory.Exists(root));
 }
 [Fact] public async Task Persistent_lock_still_fails_and_preserves_locked_file()
 {
  var root=Root();var file=Path.Combine(root,"fixture.dll");using var held=new FileStream(file,FileMode.CreateNew,FileAccess.ReadWrite,FileShare.Read);
  try{var error=await Record.ExceptionAsync(()=>NativeFixtureCleanup.DeleteAsync(root,maxAttempts:2));Assert.True(error is IOException or UnauthorizedAccessException);Assert.True(File.Exists(file));}
  finally{held.Dispose();await NativeFixtureCleanup.DeleteAsync(root);}
 }
 [Fact] public async Task Cleanup_rejects_a_non_fixture_root()=>await Assert.ThrowsAsync<ArgumentException>(()=>NativeFixtureCleanup.DeleteAsync(Path.GetTempPath()));
}
