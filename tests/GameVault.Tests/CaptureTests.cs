using GameVault.Core;
using GameVault.Infrastructure.Capture;
using GameVault.Infrastructure.Storage;
namespace GameVault.Tests;
public sealed class CaptureTests : IDisposable
{
 readonly string root = Path.Combine(Path.GetTempPath(), "gv-capture-"+Guid.NewGuid().ToString("N"));
 readonly Guid game = Guid.NewGuid();
 readonly CaptureTarget target = new(Guid.NewGuid(), 123, 456);
 async Task<CaptureCoordinator> Create(Fake backend)
 {
  Directory.CreateDirectory(root);
  var paths=ApplicationPaths.FromRoot(root); var store=new AppDataStore(paths);
  await store.InitializeAsync();
  await store.SaveProfileAsync(new(1,game,"Test",Path.Combine(root,"test.exe"),root,[],GameStatus.Playing,20,false,false));
  var now=DateTimeOffset.UtcNow; await store.SaveSessionJournalAsync(new(1,game,target.SessionId,now,now,0,SessionState.Active,target.ProcessId,target.ProcessCreatedUtcTicks,Path.Combine(root,"test.exe"),null,0));
  // Only the tests that provoke a hang need the short timeout; the others must not fail when a slow runner needs more than 100 ms to finalize.
  return new(paths,()=>backend,backend.Hang||backend.HangScreenshot?TimeSpan.FromMilliseconds(100):TimeSpan.FromSeconds(15));
 }
 [Fact] public async Task ScreenshotCommitsOnlyValidatedOutputAndSessionMetadata()
 {
  var c=await Create(new()); var media=await c.ScreenshotAsync(game,target);
  Assert.Equal(target.SessionId,media.SessionId); Assert.Equal(CaptureMediaStatus.Complete,media.Status);
  Assert.True(File.Exists(media.FilePath)); Assert.False(File.Exists(media.FilePath+".partial"));
  Assert.True(File.Exists(media.FilePath+".json")); Assert.Equal(target.SessionId,GameVault.Infrastructure.Capture.MediaFolders.Identify(Path.GetDirectoryName(media.FilePath)!));
 }
 [Fact] public async Task InvalidScreenshotRetainsPartialAndIncompleteMetadata()
 {
  var c=await Create(new(){Invalid=true}); await Assert.ThrowsAsync<IOException>(()=>c.ScreenshotAsync(game,target));
  Assert.Single(Directory.GetFiles(root,"*.partial",SearchOption.AllDirectories));
  Assert.Contains("Incomplete",File.ReadAllText(Assert.Single(Directory.GetFiles(root,"*.partial.json",SearchOption.AllDirectories))));
 }
 [Fact] public async Task StopIsIdempotentAndRequiresNativeCompletion()
 {
  var f=new Fake(); var c=await Create(f); await c.StartAsync(game,target);
  Assert.Equal(CaptureState.Recording,c.State); var media=await c.StopAsync(); Assert.Equal(CaptureMediaStatus.Complete,media!.Status); Assert.Null(await c.StopAsync());
 }
 [Fact] public async Task MissingProfileNeverInvokesBackend()
 {
  var f=new Fake(); var c=await Create(f); await Assert.ThrowsAsync<IOException>(()=>c.ScreenshotAsync(Guid.NewGuid(),target)); Assert.Equal(0,f.Calls);
 }
 [Fact] public async Task StopTimeoutKeepsPartialAndBlocksAnotherRecording()
 {
  var f=new Fake(){Hang=true}; var c=await Create(f); await c.StartAsync(game,target);
  await Assert.ThrowsAsync<TimeoutException>(()=>c.StopAsync());
  Assert.Equal(CaptureState.Failed,c.State); await Assert.ThrowsAsync<InvalidOperationException>(()=>c.StartAsync(game,target));
 }
 [Fact] public async Task ASecondStartDoesNotCorruptAnActiveRecording()
 {
  var c=await Create(new());await c.StartAsync(game,target);
  await Assert.ThrowsAsync<InvalidOperationException>(()=>c.StartAsync(game,target with{SessionId=Guid.NewGuid()}));
  Assert.Equal(CaptureState.Recording,c.State);Assert.Equal(target,c.ActiveTarget);await c.StopAsync();
 }
 [Fact] public async Task ListingNeverReturnsMetadataRedirectedOutsideItsSession()
 {
  var c=await Create(new());var media=await c.ScreenshotAsync(game,target);
  Assert.Single(await c.ListMediaAsync(game));
  var json=File.ReadAllText(media.FilePath+".json");File.WriteAllText(media.FilePath+".json",json.Replace(media.Id.ToString(),Guid.NewGuid().ToString()));
  Assert.Empty(await c.ListMediaAsync(game));
 }
 [Theory]
 [InlineData(false, false)]
 [InlineData(false, true)]
 [InlineData(true, false)]
 [InlineData(true, true)]
 public async Task CopiedLibraryResolvesCurrentMediaWithoutFollowingStoredPaths(bool legacyAbsolutePath,bool removeOriginal)
 {
  var c=await Create(new());var media=await c.ScreenshotAsync(game,target);
  if(legacyAbsolutePath)
  {
   var json=System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(media.FilePath+".json"))!;
   json["FilePath"]=media.FilePath;File.WriteAllText(media.FilePath+".json",json.ToJsonString());
  }
  var copy=Path.Combine(root,"next-build");Directory.CreateDirectory(copy);
  var original=ApplicationPaths.FromRoot(root).Profiles;
  var destination=ApplicationPaths.FromRoot(copy).Profiles;
  foreach(var directory in Directory.GetDirectories(original,"*",SearchOption.AllDirectories))
   Directory.CreateDirectory(Path.Combine(destination,Path.GetRelativePath(original,directory)));
  foreach(var file in Directory.GetFiles(original,"*",SearchOption.AllDirectories))
   File.Copy(file,Path.Combine(destination,Path.GetRelativePath(original,file)));
  if(removeOriginal)Directory.Delete(original,true);
  var copiedStore=new CaptureMediaStore(ApplicationPaths.FromRoot(copy));
  var copied=Assert.Single(await copiedStore.ListAsync(game));
  var localFile=Path.Combine(destination,Path.GetRelativePath(original,media.FilePath));
  Assert.Equal(localFile,copied.FilePath);Assert.Equal(media.Id,copied.Id);Assert.Equal(media.SessionId,copied.SessionId);
  Assert.Equal(File.ReadAllBytes(localFile),File.ReadAllBytes(copied.FilePath));
  File.Delete(localFile);
  Assert.Empty(await copiedStore.ListAsync(game));
 }
 [Theory]
 [InlineData("ProfileId", "00000000-0000-0000-0000-000000000000")]
 [InlineData("SessionId", "00000000-0000-0000-0000-000000000000")]
 [InlineData("Id", "00000000-0000-0000-0000-000000000000")]
 [InlineData("CapturedUtc", "2000-01-01T00:00:00+00:00")]
 [InlineData("Kind", "Video")]
 [InlineData("Kind", "Unknown")]
 [InlineData("Status", "Incomplete")]
 [InlineData("Status", "Unknown")]
 [InlineData("FilePath", "unrelated.png")]
 [InlineData("FilePath", null)]
 public async Task PortableMetadataCannotOverrideCaptureIdentity(string property,string? value)
 {
  var c=await Create(new());var media=await c.ScreenshotAsync(game,target);
  var sidecar=media.FilePath+".json";
  var json=System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(sidecar))!;
  json[property]=value;File.WriteAllText(sidecar,json.ToJsonString());
  Assert.Empty(await c.ListMediaAsync(game));Assert.True(File.Exists(media.FilePath));
 }
 [Fact] public async Task NewMetadataStoresOnlyThePortableMediaFilename()
 {
  var c=await Create(new());var media=await c.ScreenshotAsync(game,target);
  var json=System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(media.FilePath+".json"))!;
  Assert.Equal(Path.GetFileName(media.FilePath),json["FilePath"]!.GetValue<string>());
 }
 [Fact] public async Task MetadataWriteFailurePreservesDiscoverableIncompleteOutput()
 {
  await Create(new());var store=new CaptureMediaStore(ApplicationPaths.FromRoot(root));
  var media=await store.BeginAsync(game,target,CaptureMediaKind.Screenshot);
  await new Fake().ScreenshotAsync(target,media.FilePath,CancellationToken.None);
  Directory.CreateDirectory(media.FilePath[..^8]+".json");
  var error=Record.Exception(()=>store.Complete(media));Assert.True(error is IOException or UnauthorizedAccessException);
  Assert.True(File.Exists(media.FilePath));
  var incomplete=Assert.Single(await store.ListAsync(game));
  Assert.Equal(CaptureMediaStatus.Incomplete,incomplete.Status);Assert.Equal(media.FilePath,incomplete.FilePath);
 }
 [Fact] public async Task InvalidOwnershipCannotRenameIncompleteMediaBeforeValidation()
 {
  await Create(new());var store=new CaptureMediaStore(ApplicationPaths.FromRoot(root));
  var media=await store.BeginAsync(game,target,CaptureMediaKind.Screenshot);
  await new Fake().ScreenshotAsync(target,media.FilePath,CancellationToken.None);
  Assert.Throws<IOException>(()=>store.Complete(media with{ProfileId=Guid.NewGuid()}));
  Assert.True(File.Exists(media.FilePath));Assert.Single(await store.ListAsync(game));
 }
 [Fact] public async Task TruncatedMediaDoesNotCommitEvenWhenItsSignatureLooksValid()
 {
  var c=await Create(new(){Truncated=true});await Assert.ThrowsAsync<IOException>(()=>c.ScreenshotAsync(game,target));
 }
 [Fact] public async Task FailedFactoryLeavesIncompleteMediaAndAllowsLaterRetry()
 {
  await Create(new());var attempts=0;var c=new CaptureCoordinator(ApplicationPaths.FromRoot(root),()=>++attempts==1?throw new DllNotFoundException("test"):new Fake());
  await Assert.ThrowsAsync<DllNotFoundException>(()=>c.StartAsync(game,target));
  await c.StartAsync(game,target);Assert.Equal(CaptureState.Recording,c.State);await c.StopAsync();
 }
 [Fact] public async Task OptionsAreBoundedBeforeBackendOrMediaCreation()
 {
  var f=new Fake();var c=await Create(f);
  await Assert.ThrowsAsync<ArgumentException>(()=>c.StartAsync(game,target,new(Width:4096)));
  Assert.Equal(0,f.Calls);Assert.Empty(Directory.GetFiles(root,"*.partial",SearchOption.AllDirectories));
 }
 [Fact] public async Task TargetMustMatchTheProfilesAuthoritativeJournal()
 {
  var f=new Fake();var c=await Create(f);
  await Assert.ThrowsAsync<IOException>(()=>c.ScreenshotAsync(game,target with{SessionId=Guid.NewGuid()}));
  await Assert.ThrowsAsync<IOException>(()=>c.ScreenshotAsync(game,target with{ProcessCreatedUtcTicks=999}));
  Assert.Equal(0,f.Calls);
 }
 [Fact] public async Task VideoWithOnlyAFileTypeHeaderRemainsIncomplete()
 {
  var c=await Create(new(){BadVideo=true});await c.StartAsync(game,target);
  await Assert.ThrowsAsync<IOException>(()=>c.StopAsync());
  Assert.Null(c.ActiveTarget);
  Assert.Equal(CaptureMediaStatus.Incomplete,Assert.Single(await c.ListMediaAsync(game)).Status);
 }
 [Fact] public async Task KnownFinishedBackendFailureAllowsRetryWhilePreservingIncompleteOutput()
 {
  await Create(new());var c=new CaptureCoordinator(ApplicationPaths.FromRoot(root),()=>new FinishedFailure());
  await Assert.ThrowsAsync<IOException>(()=>c.StartAsync(game,target));
  Assert.Null(c.ActiveTarget);Assert.Equal(CaptureState.Failed,c.State);
  Assert.Equal(CaptureMediaStatus.Incomplete,Assert.Single(await c.ListMediaAsync(game)).Status);
 }
 sealed class FinishedFailure : ICaptureBackend,ICaptureBackendLifetime
 {
  public Task Completion=>Task.CompletedTask;
  public Task ScreenshotAsync(CaptureTarget t,string p,CancellationToken ct)=>throw new IOException("test");
  public Task StartAsync(CaptureTarget t,string p,CaptureOptions o,CancellationToken ct)=>throw new IOException("test");
  public Task StopAsync(CancellationToken ct)=>Task.CompletedTask;
 }
 [Fact] public async Task ScreenshotFailureDoesNotDisturbTheActiveVideo()
 {
  var f=new Fake(){Invalid=true};var c=await Create(f);await c.StartAsync(game,target);
  await Assert.ThrowsAsync<IOException>(()=>c.ScreenshotAsync(game,target));
  Assert.Equal(CaptureState.Recording,c.State);Assert.Equal(target,c.ActiveTarget);
  Assert.Equal(game,c.ActiveProfileId);Assert.NotNull(await c.StopAsync());
 }
 [Fact] public async Task ScreenshotAndVideoRemainLinkedToTheSameSession()
 {
  var c=await Create(new());await c.StartAsync(game,target);var screenshot=await c.ScreenshotAsync(game,target);
  Assert.Equal(CaptureMediaKind.Screenshot,screenshot.Kind);Assert.Equal(CaptureState.Recording,c.State);
  var video=await c.StopAsync();Assert.Equal(screenshot.SessionId,video!.SessionId);Assert.Equal(2,(await c.ListMediaAsync(game)).Length);
 } [Fact] public async Task UnknownScreenshotWriterBlocksFurtherCaptureUntilItFinishes()
 {
  var c=await Create(new(){HangScreenshot=true});
  await Assert.ThrowsAsync<TimeoutException>(()=>c.ScreenshotAsync(game,target));
  await Assert.ThrowsAsync<InvalidOperationException>(()=>c.ScreenshotAsync(game,target));
  await Assert.ThrowsAsync<InvalidOperationException>(()=>c.StartAsync(game,target));
 } public void Dispose(){if(Directory.Exists(root))Directory.Delete(root,true);}
 sealed class Fake : ICaptureBackend
 {
  public bool Invalid,Hang,Truncated,BadVideo,HangScreenshot; public int Calls; string? path;
  public Task ScreenshotAsync(CaptureTarget target,string path,CancellationToken ct){Calls++; File.WriteAllBytes(path,Invalid?[1,2]:Truncated?[137,80,78,71,13,10,26,10,0,0,0,0,73,72,68,82]:Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAEAAAABACAYAAACqaXHeAAAAmElEQVR4nO3QMREAIBDAsJeEJKTiDGRkoEP2Xmftc382OkBrgA7QGqADtAboAK0BOkBrgA7QGqADtAboAK0BOkBrgA7QGqADtAboAK0BOkBrgA7QGqADtAboAK0BOkBrgA7QGqADtAboAK0BOkBrgA7QGqADtAboAK0BOkBrgA7QGqADtAboAK0BOkBrgA7QGqADtAboAK0BOkBrgA7QGqADtAboAO0BYkPyHU0laccAAAAASUVORK5CYII="));return HangScreenshot?Task.Delay(Timeout.Infinite):Task.CompletedTask;}
  public Task StartAsync(CaptureTarget target,string path,CaptureOptions options,CancellationToken ct){Calls++;this.path=path;File.WriteAllBytes(path,BadVideo?[0,0,0,16,102,116,121,112,105,115,111,109,0,0,0,0]:[0,0,0,16,102,116,121,112,105,115,111,109,0,0,0,0,0,0,0,8,109,111,111,118,0,0,0,9,109,100,97,116,1]);return Task.CompletedTask;}
  public Task StopAsync(CancellationToken ct)=>Hang?Task.Delay(Timeout.Infinite,ct):Task.CompletedTask;
 }
}
