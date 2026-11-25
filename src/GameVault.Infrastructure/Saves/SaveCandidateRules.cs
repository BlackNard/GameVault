using System.Runtime.InteropServices;
using GameVault.Core;
namespace GameVault.Infrastructure.Saves;
public sealed record SaveDiscoveryEnvironment(string Home,string Documents,string SavedGames,string Roaming,string Local,string PublicDocuments,string[] SteamRoots,ISaveLocationSource? Database=null)
{
    public string[] BroadRoots=>[Home,Documents,SavedGames,Roaming,Local,PublicDocuments,..SteamRoots.Select(r=>Path.Combine(r,"userdata"))];
    public static SaveDiscoveryEnvironment Current()
    {
        var home=Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);var saved=Path.Combine(home,"Saved Games");
        if(OperatingSystem.IsWindows())
        {
            var id=new Guid("4C5C32FF-BB9D-43B0-B5B4-2D72E54EAAA4");IntPtr pointer=IntPtr.Zero;
            try{if(SHGetKnownFolderPath(ref id,0,IntPtr.Zero,out pointer)==0)saved=Marshal.PtrToStringUni(pointer)??saved;}
            finally{if(pointer!=IntPtr.Zero)Marshal.FreeCoTaskMem(pointer);}
        }
        return new(home,Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),saved,Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),Environment.GetFolderPath(Environment.SpecialFolder.CommonDocuments),
            [Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),"Steam"),Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"Steam")]);
    }
    [DllImport("shell32.dll")]private static extern int SHGetKnownFolderPath(ref Guid id,uint flags,IntPtr token,out IntPtr path);
}
public sealed record SaveCandidate(string Path,SaveSourceOrigin Origin,string Evidence,string Payload="any",bool KnownLocation=false);
public sealed record SaveCandidateCatalog(SaveCandidate[] Candidates,string[] Warnings);
public static class SaveCandidateRules
{
    public static SaveCandidateCatalog For(GameProfile profile,SaveDiscoveryEnvironment env)
    {
        var candidates=new List<SaveCandidate>();var warnings=new List<string>();var folder=Path.GetDirectoryName(profile.ExecutablePath)!;
        var names=new[]{profile.Name,Path.GetFileName(folder)}.Where(SafeName).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var normalized=names.Select(Normalize).ToHashSet();var exe=Path.GetFileNameWithoutExtension(profile.ExecutablePath);
        if(exe.Equals("re2",StringComparison.OrdinalIgnoreCase)&&normalized.Any(n=>n is "residentevil2" or "residentevil22019" or "residentevil2remake" or "residentevil2biohazardre2"))
        {
            Add(Path.Combine(env.PublicDocuments,"Steam","CODEX","883710","remote","win64_save"),SaveSourceOrigin.KnownRule,"RE2 (2019): game-specific public save directory","bin",true);
            foreach(var name in new[]{"Goldberg SteamEmu Saves","EMPRESS"})
                Add(Path.Combine(env.Roaming,name,"883710","remote","win64_save"),SaveSourceOrigin.KnownRule,"RE2 (2019): game-specific local save directory","bin",true);
            var roots=env.SteamRoots.AsEnumerable();var marker=folder.IndexOf(Path.DirectorySeparatorChar+"steamapps"+Path.DirectorySeparatorChar+"common"+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);
            if(marker>0)roots=roots.Append(folder[..marker]);
            foreach(var steam in roots.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var users=Path.Combine(steam,"userdata");
                try
                {
                    SaveSourcePolicy.CheckLinks(users);if(!Directory.Exists(users))continue;
                    var accounts=Directory.EnumerateDirectories(users).Take(33).ToArray();if(accounts.Length>32)warnings.Add("Steam account search reached its limit; confirm a source or choose a manual path.");
                    foreach(var account in accounts.Take(32))
                    {
                        SaveSourcePolicy.CheckLinks(account);if(!Path.GetFileName(account).All(char.IsAsciiDigit))continue;
                        Add(Path.Combine(account,"883710","remote","win64_save"),SaveSourceOrigin.KnownRule,"RE2 (2019): account-specific save directory","bin",true);
                    }
                }
                catch(Exception e)when(e is IOException or UnauthorizedAccessException){warnings.Add("Steam userdata could not be checked safely. "+e.Message);}
            }
        }
        if(normalized.Any(n=>n is "deadspace" or "deadspace2008" or "deadspace2023" or "deadspaceremake")&&Normalize(exe)=="deadspace")
        {
            var original=normalized.Contains("deadspace2008");var remake=normalized.Contains("deadspace2023")||normalized.Contains("deadspaceremake");
            Add(Path.Combine(env.Documents,"Electronic Arts","Dead Space"),original?SaveSourceOrigin.KnownRule:SaveSourceOrigin.Suggestion,
                "Dead Space (2008): known location"+(original?"":"; confirm the game version"),"any",true);
            Add(Path.Combine(env.Documents,"Dead Space (2023)","settings"),remake?SaveSourceOrigin.KnownRule:SaveSourceOrigin.Suggestion,
                "Dead Space (2023): known saves/settings location"+(remake?"":"; confirm the game version"),"any",true);
        }
        // Rockstar games save under a short name, not the game's title (issue #73).
        foreach(var title in RockstarTitle.All.Where(t=>t.Matches(Normalize(exe),normalized)))
        {
            Add(Path.Combine(env.Documents,"Rockstar Games",title.Folder,"Profiles"),SaveSourceOrigin.KnownRule,title.Folder+": Rockstar profile saves","save",true);
            // Copies that run without the Rockstar launcher keep the same files under the replacement service's own folder.
            Add(Path.Combine(env.Roaming,"Goldberg SocialClub Emu Saves",title.Folder),SaveSourceOrigin.KnownRule,title.Folder+": saves of installations without the Rockstar launcher","save",true);
            if(title.LocalSaves)Add(Path.Combine(env.Local,"Rockstar Games",title.Folder,"savegames"),SaveSourceOrigin.KnownRule,title.Folder+": saves of older installations","save",true);
        }
        foreach(var name in names)
        {
            Add(Path.Combine(env.Documents,"Rockstar Games",name,"Profiles"),SaveSourceOrigin.Suggestion,"Game-named Rockstar profile folder; confirmation required","save");
            foreach(var root in new[]{env.SavedGames,Path.Combine(env.Documents,"My Games"),env.Documents})
                Add(Path.Combine(root,name),SaveSourceOrigin.Suggestion,"Game-named folder candidate; confirmation required","save");
            Add(Path.Combine(env.Local,name,"Saved","SaveGames"),SaveSourceOrigin.Suggestion,"Game-named Unreal save folder; confirmation required","save");
            Add(Path.Combine(env.Roaming,name,"saves"),SaveSourceOrigin.Suggestion,"Game-named application save folder; confirmation required","save");
        }
        foreach(var leaf in new[]{"saves","save","SaveGames","Saved/SaveGames"})
            Add(Path.Combine(folder,leaf.Replace('/',Path.DirectorySeparatorChar)),SaveSourceOrigin.Suggestion,"Install-folder save candidate; confirmation required","save");
        // Community database (only after the owner downloaded it). Always a suggestion: the usual confirmation decides.
        if(env.Database is not null)
        {
            try{foreach(var path in env.Database.Candidates(profile,env))Add(path,SaveSourceOrigin.Suggestion,"Save-location database (PCGamingWiki via Ludusavi); confirmation required","any");}
            catch(Exception e)when(e is IOException or UnauthorizedAccessException or ArgumentException){warnings.Add("The save-location database could not be read. "+e.Message);}
        }
        return new(candidates.DistinctBy(c=>SaveSourcePolicy.Canonical(c.Path),StringComparer.OrdinalIgnoreCase).Take(96).ToArray(),warnings.Take(64).ToArray());
        void Add(string path,SaveSourceOrigin origin,string evidence,string payload,bool known=false)=>candidates.Add(new(path,origin,evidence,payload,known));
    }
    /// <summary>A Rockstar game recognised by its program or title, and the folder it saves under.</summary>
    private sealed record RockstarTitle(string Folder,string[] Programs,Func<string,bool> Titled,bool LocalSaves=false)
    {
        public static readonly RockstarTitle[] All=
        [
            new("GTA IV",["gtaiv","eflc","launchgtaiv","playgtaiv","launcheflc"],n=>n.StartsWith("grandtheftautoiv",StringComparison.Ordinal)||n.StartsWith("gtaiv",StringComparison.Ordinal)||n.EndsWith("episodesfromlibertycity",StringComparison.Ordinal),true),
            new("GTA V",["gta5","playgtav"],n=>n is "grandtheftautov" or "grandtheftautovlegacy" or "grandtheftautovpremiumedition" or "gtav" or "gtavlegacy"),
            new("GTAV Enhanced",["gta5enhanced"],n=>n is "grandtheftautovenhanced" or "gtavenhanced"),
            new("Red Dead Redemption 2",["rdr2"],n=>n is "reddeadredemption2" or "rdr2")
        ];
        public bool Matches(string program,HashSet<string> titles)=>Programs.Contains(program)||titles.Any(Titled);
    }
    private static string Normalize(string value)=>new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
    private static bool SafeName(string value)=>!string.IsNullOrWhiteSpace(value)&&value.Length<=120&&value.IndexOfAny(Path.GetInvalidFileNameChars())<0&&
        value is not "." and not ".."&&Normalize(value).Length>=3&&!new[]{"game","games","documents","savedgames","appdata","public","user","users","bin","binaries","win64","windows","steam"}.Contains(Normalize(value));
    public static bool PayloadMatches(string path,string kind)=>kind switch
    {
        "bin"=>Path.GetExtension(path).Equals(".bin",StringComparison.OrdinalIgnoreCase),
        "save"=>IsRockstarSave(path)||!Path.GetFileName(path).Equals("pc_settings.bin",StringComparison.OrdinalIgnoreCase)&&
            new[]{".sav",".save",".savegame",".dat",".bin",".deadspacesaved",".json"}.Contains(Path.GetExtension(path),StringComparer.OrdinalIgnoreCase),_=>true
    };
    /// <summary>Rockstar save files have no extension: SGTA400 (GTA IV), SGTA50000 (GTA V), SRDR30000 (Red Dead Redemption 2).</summary>
    private static bool IsRockstarSave(string path)
    {
        var name=Path.GetFileName(path);
        return name.Length is >=7 and <=12&&(name.StartsWith("SGTA",StringComparison.OrdinalIgnoreCase)||name.StartsWith("SRDR",StringComparison.OrdinalIgnoreCase))&&name.Skip(4).All(char.IsAsciiDigit);
    }
}