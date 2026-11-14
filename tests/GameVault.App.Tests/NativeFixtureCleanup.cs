using System.IO;
namespace GameVault.App.Tests;

/// <summary>Windows can briefly retain an exited image mapping. Retry only this test-owned GUID directory.</summary>
internal static class NativeFixtureCleanup
{
 public static async Task DeleteAsync(string root,Action? retryObserved=null,int maxAttempts=50)
 {
  var full=Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
  var parent=Path.Combine(Path.GetFullPath(Path.GetTempPath()),"GameVault native fixtures").TrimEnd(Path.DirectorySeparatorChar);
  if(!string.Equals(Path.GetDirectoryName(full),parent,StringComparison.OrdinalIgnoreCase)||!Guid.TryParseExact(Path.GetFileName(full),"N",out _)||maxAttempts is <1 or >100)
   throw new ArgumentException("Only a test-owned native fixture GUID directory may be removed.",nameof(root));
  for(var attempt=1;;attempt++)
  {
   try{if(Directory.Exists(full))Directory.Delete(full,true);return;}
   catch(Exception error)when((error is IOException or UnauthorizedAccessException)&&attempt<maxAttempts)
   {
    retryObserved?.Invoke();await Task.Delay(100);
   }
  }
 }
}
