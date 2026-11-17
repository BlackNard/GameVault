using System.Buffers.Binary;
using System.Text.Json;
using System.Text.Json.Serialization;
using GameVault.Core;
using GameVault.Infrastructure.Storage;
namespace GameVault.Infrastructure.Capture;

public sealed class CaptureMediaStore(ApplicationPaths paths)
{
 readonly ManagedPaths managed = new(paths);
 static readonly JsonSerializerOptions Json = new(){Converters={new JsonStringEnumConverter()}};
 public async Task<CaptureMedia> BeginAsync(Guid profileId,CaptureTarget target,CaptureMediaKind kind)
 {
  if(profileId==Guid.Empty || target.SessionId==Guid.Empty || target.ProcessId<=0 || target.ProcessCreatedUtcTicks<=0)
   throw new ArgumentException("An actual running session is required for capture.");
  var profile=await new AppDataStore(paths).LoadProfileAsync(profileId);
  if(profile.Profile.State!=DocumentState.Ready || profile.Profile.Value!.IsArchived)
   throw new IOException("A writable library profile is required for capture.");
  var journals=await new AppDataStore(paths).LoadSessionJournalsAsync(profileId);
  var journal=journals.SingleOrDefault(j=>j.Id==target.SessionId)?.Document;
  if(journal?.State!=DocumentState.Ready || journal.Value is not {State:SessionState.Active} session ||
   session.ProcessId!=target.ProcessId || session.ProcessCreatedUtcTicks!=target.ProcessCreatedUtcTicks)
   throw new IOException("Capture target does not match this profile's active saved game session.");
  var folder=Path.Combine(ProfileFolders.Resolve(paths,profileId),"Media",target.SessionId.ToString("N"));
  managed.EnsureDirectory(folder);
  var now=DateTimeOffset.UtcNow; var id=Guid.NewGuid();
  var name=$"{now:yyyyMMddTHHmmssfffffffZ}-{id:N}"+(kind==CaptureMediaKind.Video?".mp4":".png");
  var media=new CaptureMedia(id,profileId,target.SessionId,now,kind,CaptureMediaStatus.Incomplete,Path.Combine(folder,name)+".partial");
  managed.Check(media.FilePath);
  using(var reservation=new FileStream(media.FilePath,FileMode.CreateNew,FileAccess.Write,FileShare.None)) reservation.Flush(true);
  Write(media); return media;
 }
 /// <summary>Reserves an entry for a clip copied from another recorder. Unlike a live capture it needs no active session, only a writable profile.</summary>
 public async Task<CaptureMedia> BeginImportAsync(Guid profileId,Guid sessionId,CaptureMediaKind kind,DateTimeOffset capturedUtc,string sourceFingerprint)
 {
  if(profileId==Guid.Empty || sessionId==Guid.Empty || string.IsNullOrWhiteSpace(sourceFingerprint) || sourceFingerprint.Length>128)throw new ArgumentException("Invalid import request.");
  var profile=await new AppDataStore(paths).LoadProfileAsync(profileId);
  if(profile.Profile.State!=DocumentState.Ready || profile.Profile.Value!.IsArchived)
   throw new IOException("A writable library profile is required to import captures.");
  var folder=Path.Combine(ProfileFolders.Resolve(paths,profileId),"Media",sessionId.ToString("N"));
  managed.EnsureDirectory(folder);
  var id=Guid.NewGuid();
  var name=$"{capturedUtc.ToUniversalTime():yyyyMMddTHHmmssfffffffZ}-{id:N}"+(kind==CaptureMediaKind.Video?".mp4":".png");
  var media=new CaptureMedia(id,profileId,sessionId,capturedUtc.ToUniversalTime(),kind,CaptureMediaStatus.Incomplete,Path.Combine(folder,name)+".partial",null,sourceFingerprint);
  managed.Check(media.FilePath);
  using(var reservation=new FileStream(media.FilePath,FileMode.CreateNew,FileAccess.Write,FileShare.None)) reservation.Flush(true);
  Write(media); return media;
 }
 /// <summary>Removes an unfinished imported copy and its metadata. Only for imports: the original recording is never touched.</summary>
 public void Discard(CaptureMedia media)
 {
  CheckMedia(media);
  if(media.Status!=CaptureMediaStatus.Incomplete || media.SourceFingerprint is null)throw new ArgumentException("Only unfinished imported captures can be discarded.");
  File.Delete(media.FilePath);File.Delete(media.FilePath+".json");
 }
 public Task<CaptureMedia[]> ListAsync(Guid profileId)=>Task.Run(async()=>
 {
  if(profileId==Guid.Empty)throw new ArgumentException("A profile is required.");
  var profile=await new AppDataStore(paths).LoadProfileAsync(profileId);
  if(profile.Profile.State!=DocumentState.Ready)throw new IOException("This profile needs attention before its media can be read.");
  var folder=Path.Combine(ProfileFolders.Resolve(paths,profileId),"Media"); managed.Check(folder);
  if(!Directory.Exists(folder))return Array.Empty<CaptureMedia>();
  var result=new List<CaptureMedia>();
  foreach(var session in Directory.EnumerateDirectories(folder))
  {
   if(!Guid.TryParseExact(Path.GetFileName(session),"N",out var sessionId))continue;
   managed.Check(session);
   foreach(var file in Directory.EnumerateFiles(session,"*.json"))
   {
    managed.Check(file);if(new FileInfo(file).Length>65536)continue;
    try
    {
     var media=JsonSerializer.Deserialize<CaptureMedia>(File.ReadAllText(file),Json);
     if(media is null || media.ProfileId!=profileId || media.SessionId!=sessionId || sessionId==Guid.Empty || media.Id==Guid.Empty ||
      !Enum.IsDefined(media.Kind) || !Enum.IsDefined(media.Status) || media.CapturedUtc==default ||
      string.IsNullOrWhiteSpace(media.FilePath))continue;
     var extension=media.Kind==CaptureMediaKind.Video?".mp4":".png";
     if(media.Status==CaptureMediaStatus.Incomplete)extension+=".partial";
     var name=$"{media.CapturedUtc:yyyyMMddTHHmmssfffffffZ}-{media.Id:N}"+extension;
     if(!Path.GetFileName(file).Equals(name+".json",StringComparison.OrdinalIgnoreCase) ||
      !Path.GetFileName(media.FilePath).Equals(name,StringComparison.OrdinalIgnoreCase) ||
      (!Path.IsPathFullyQualified(media.FilePath) && !media.FilePath.Equals(name,StringComparison.OrdinalIgnoreCase)))continue;
     // Legacy absolute paths contribute only their validated basename; never follow the old location.
     media=media with{FilePath=Path.Combine(session,name)};
     CheckMedia(media);
     if(!File.Exists(media.FilePath))continue;
     result.Add(media);
    }
    catch(JsonException){ /* Preserve unrecognized/corrupt metadata for recovery. */ }
   }
  }
  return result.OrderByDescending(m=>m.CapturedUtc).ToArray();
 });
 public CaptureMedia Complete(CaptureMedia media)
 {
  CheckMedia(media);
  if(media.Status!=CaptureMediaStatus.Incomplete)throw new ArgumentException("Only incomplete capture output can be completed.");
  using(var file=File.OpenRead(media.FilePath))
  {
   Span<byte> header=stackalloc byte[16]; if(file.Read(header)!=16)throw new IOException("Capture is empty or incomplete; partial output was preserved.");
   bool valid=media.Kind==CaptureMediaKind.Screenshot
    ?header[..8].SequenceEqual(new byte[]{137,80,78,71,13,10,26,10}) && header[12..16].SequenceEqual("IHDR"u8)
    :header[4..8].SequenceEqual("ftyp"u8);
   if(media.Kind==CaptureMediaKind.Screenshot && valid)
   {
    valid=file.Length>=45;
    if(valid){file.Seek(-12,SeekOrigin.End);Span<byte> end=stackalloc byte[12];valid=file.Read(end)==12 && end[..8].SequenceEqual(new byte[]{0,0,0,0,73,69,78,68});}
   }
   if(media.Kind==CaptureMediaKind.Video && valid)valid=ValidMp4(file);
   if(!valid)throw new IOException("Capture output has an invalid media header; partial output was preserved.");
  }
  var complete=media with{Status=CaptureMediaStatus.Complete,FilePath=media.FilePath[..^8]};
  managed.Check(complete.FilePath);
  // Persist metadata first so a write failure leaves the existing partial output discoverable.
  Write(complete); File.Move(media.FilePath,complete.FilePath,false);
  File.Delete(media.FilePath+".json"); return complete;
 }
 public void Fail(CaptureMedia media,Exception error)=>Write(media with{Error=error.Message});
 static bool ValidMp4(Stream file)
 {
  bool ftyp=false,moov=false,mdat=false;file.Position=0;Span<byte> box=stackalloc byte[16];
  for(var count=0;file.Position<file.Length && count<1_000_000;count++)
  {
   var start=file.Position;
   if(file.Read(box[..8])!=8)return false;
   long size=BinaryPrimitives.ReadUInt32BigEndian(box[..4]);var header=8;
   if(size==1)
   {
    if(file.Read(box[8..])!=8)return false;
    var extended=BinaryPrimitives.ReadUInt64BigEndian(box[8..]);if(extended>long.MaxValue)return false;
    size=(long)extended;header=16;
   }
   if(size==0)size=file.Length-start;
   if(size<header || size>file.Length-start)return false;
   if(box[4..8].SequenceEqual("ftyp"u8))ftyp=size>=16;
   if(box[4..8].SequenceEqual("moov"u8))moov=true;
   if(box[4..8].SequenceEqual("mdat"u8))mdat=size>header;
   file.Position=start+size;
  }
  return file.Position==file.Length && ftyp && moov && mdat;
 }
 void CheckMedia(CaptureMedia media)
 {
  if(media.ProfileId==Guid.Empty || media.SessionId==Guid.Empty || media.Id==Guid.Empty ||
   !Enum.IsDefined(media.Status) || !Enum.IsDefined(media.Kind) || media.CapturedUtc==default)
   throw new ArgumentException("Invalid capture metadata.");
  var extension=media.Kind==CaptureMediaKind.Video?".mp4":".png";
  if(media.Status==CaptureMediaStatus.Incomplete)extension+=".partial";
  var expected=Path.Combine(ProfileFolders.Resolve(paths,media.ProfileId),"Media",media.SessionId.ToString("N"),
   $"{media.CapturedUtc:yyyyMMddTHHmmssfffffffZ}-{media.Id:N}"+extension);
  if(!string.Equals(expected,media.FilePath,StringComparison.OrdinalIgnoreCase))throw new IOException("Capture media must remain in its owning profile and session folder.");
  managed.Check(media.FilePath);
 } void Write(CaptureMedia media)
 {
  CheckMedia(media);var path=media.FilePath+".json"; managed.Check(path);
  var temp=path+".tmp-"+Guid.NewGuid().ToString("N"); managed.Check(temp);
  using(var stream=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None))
  {JsonSerializer.Serialize(stream,media with{FilePath=Path.GetFileName(media.FilePath)},Json);stream.Flush(true);}
  File.Move(temp,path,true);
 }
}
