using System.Diagnostics;
using GameVault.Core;
using GameVault.Infrastructure.Library;
using GameVault.Infrastructure.Storage;
Console.WriteLine($"OS={Environment.OSVersion}; runtime={Environment.Version}; CPUs={Environment.ProcessorCount}; 64bit={Environment.Is64BitProcess}");
foreach (var size in new[] { 500, 1000 }) {
for (var repetition=0; repetition<5; repetition++) {
 var root = Path.Combine(Path.GetTempPath(), "GameVault-scale-" + Guid.NewGuid().ToString("N"));
 Directory.CreateDirectory(root);
 try {
  var paths=ApplicationPaths.FromRoot(root); var store=new AppDataStore(paths); await store.InitializeAsync();
  var exe=Path.Combine(root,"fixture.exe"); File.WriteAllText(exe,"not executed");
  for(var i=0;i<size;i++) await store.SaveProfileAsync(new GameProfile(1,Guid.NewGuid(),$"Synthetic game {i:0000}",exe,root,[],GameStatus.NotStarted,10,false,false));
  var service=new LibraryService(paths,new AppDataStore(paths));
  var sw=Stopwatch.StartNew(); var snapshot=await service.ListAsync(); sw.Stop(); var cold=sw.Elapsed.TotalMilliseconds;
  var samples=new List<double>(); var query=new List<double>();
  for(var i=0;i<15;i++) { sw.Restart(); snapshot=await service.ListAsync(); sw.Stop(); samples.Add(sw.Elapsed.TotalMilliseconds); }
  for(var i=0;i<100;i++) { sw.Restart(); var matches=snapshot.Games.Where(g => g.Profile?.Name.Contains("game 00",StringComparison.OrdinalIgnoreCase)==true).ToArray(); sw.Stop(); query.Add(sw.Elapsed.TotalMilliseconds); GC.KeepAlive(matches); }
  GC.Collect(); GC.WaitForPendingFinalizers();
  samples.Sort(); query.Sort();
  Console.WriteLine($"n={size} repetition={repetition} first-storage-read-ms={cold:F2}; warm median={samples[7]:F2} p95={samples[14]:F2}; query median={query[49]:F4} p95={query[94]:F4}; managed-bytes={GC.GetTotalMemory(false)}; working-set={Environment.WorkingSet}");
 } finally { Directory.Delete(root,true); }
}

}
