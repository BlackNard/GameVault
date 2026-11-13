using System.Security.Cryptography;
using System.Text;
namespace GameVault.Core;
public enum SaveSourceKind { File, Folder }
public enum SaveSourceOrigin { Manual, KnownRule, Suggestion, Confirmed }
public enum SaveSourceAccess { Ready, Missing, Empty, Unreadable, Unsafe, Limited, NotChecked }
public enum SaveDiscoveryPhase { WaitingForSave, SourcesFound, NeedsConfirmation, Unresolved, Disabled, Error }
public sealed record SaveSourceMapping(Guid Id,string Path,SaveSourceKind Kind,SaveSourceOrigin Origin,bool Selected,
    SaveSourceAccess Access,string Evidence,string? Message,bool CanConfirm=false,string Payload="any");
public sealed record SaveDiscoveryDocument(int SchemaVersion,Guid GameId,string Fingerprint,bool Automatic,
    DateTimeOffset ScannedAt,SaveSourceMapping[] Sources,string[] Warnings,long Revision=1);
public sealed record SaveDiscoveryResult(StoredDocument<SaveDiscoveryDocument> Document,SaveDiscoveryPhase Phase,string Summary);
public static class SaveDiscoveryIdentity
{
    public static string For(GameProfile profile)
    {
        static string Normalize(string path)=>Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)).ToUpperInvariant();
        var value=Normalize(profile.ExecutablePath)+"\n"+Normalize(profile.WorkingDirectory)+"\n"+
            string.Join("\n",profile.SaveSources.Select(Normalize).Distinct().Order(StringComparer.Ordinal));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }
}