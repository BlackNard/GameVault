using GameVault.Core;

namespace GameVault.Tests;

public sealed class DroppedGameResolverTests
{
    private sealed class Disk
    {
        public readonly HashSet<string> Files = new(StringComparer.OrdinalIgnoreCase);
        public readonly HashSet<string> Folders = new(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, string?> Shortcuts = new(StringComparer.OrdinalIgnoreCase);
        public readonly HashSet<string> Library = new(StringComparer.OrdinalIgnoreCase);
        public readonly HashSet<string> Unreadable = new(StringComparer.OrdinalIgnoreCase);

        public DropResolution Drop(string path) => DroppedGameResolver.Resolve(path,
            p => Shortcuts.TryGetValue(p, out var target) ? target : throw new IOException("not a shortcut"),
            p => Files.Contains(p), p => Folders.Contains(p),
            folder => Unreadable.Contains(folder) ? throw new UnauthorizedAccessException() : Files.Where(f => string.Equals(Path.GetDirectoryName(f), folder, StringComparison.OrdinalIgnoreCase) && f.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)),
            folder => Folders.Where(f => string.Equals(Path.GetDirectoryName(f), folder, StringComparison.OrdinalIgnoreCase)),
            exe => Library.Contains(exe));
    }

    private const string Hades = @"D:\Games\Hades\Hades.exe";

    [Fact]
    public void An_existing_exe_is_accepted_a_missing_one_and_other_files_are_not()
    {
        var disk = new Disk();
        disk.Files.Add(Hades);
        Assert.Equal(new DropResolution(DropOutcome.Accepted, Hades), disk.Drop(Hades));
        Assert.Equal(DropOutcome.Accepted, disk.Drop("\"" + Hades + "\"").Outcome);                       // quotes from a paste are tolerated
        Assert.Equal(DropOutcome.Unsupported, disk.Drop(@"D:\Games\Gone.exe").Outcome);                   // dropped, then deleted
        foreach (var other in new[] { @"D:\notes.txt", @"D:\Games\Hades\readme.pdf", @"D:\Games\game.url", @"D:\Games\game.bat", @"D:\Games\Hades\Hades.dll" })
        {
            disk.Files.Add(other);
            Assert.Equal(DropOutcome.Unsupported, disk.Drop(other).Outcome);
        }
        Assert.Equal(DropOutcome.Unsupported, disk.Drop("").Outcome);
        Assert.Equal(DropOutcome.Unsupported, disk.Drop("bad\0path.exe").Outcome);
    }

    [Fact]
    public void A_game_that_is_already_in_the_library_is_recognised_and_not_added_twice()
    {
        var disk = new Disk();
        disk.Files.Add(Hades);
        disk.Library.Add(Hades);
        Assert.Equal(new DropResolution(DropOutcome.AlreadyInLibrary, Hades), disk.Drop(Hades));
        disk.Files.Add(@"D:\Desktop\Hades.lnk");
        disk.Shortcuts[@"D:\Desktop\Hades.lnk"] = Hades;
        Assert.Equal(DropOutcome.AlreadyInLibrary, disk.Drop(@"D:\Desktop\Hades.lnk").Outcome);           // also through a shortcut
    }

    [Fact]
    public void A_shortcut_is_followed_to_its_exe_and_a_broken_one_says_so()
    {
        var disk = new Disk();
        disk.Files.Add(Hades);
        disk.Shortcuts[@"D:\Desktop\Hades.lnk"] = Hades;
        Assert.Equal(new DropResolution(DropOutcome.Accepted, Hades), disk.Drop(@"D:\Desktop\Hades.lnk"));
        disk.Shortcuts[@"D:\Desktop\Gone.lnk"] = @"D:\Games\Gone\Gone.exe";            // target no longer exists
        disk.Shortcuts[@"D:\Desktop\Folder.lnk"] = @"D:\Games\Hades";                  // points to a folder, not an exe
        disk.Shortcuts[@"D:\Desktop\Doc.lnk"] = @"D:\Docs\a.docx";
        disk.Shortcuts[@"D:\Desktop\Empty.lnk"] = null;
        disk.Shortcuts[@"D:\Desktop\Blank.lnk"] = "  ";
        foreach (var name in new[] { "Gone", "Folder", "Doc", "Empty", "Blank" })
            Assert.Equal(DropOutcome.BrokenShortcut, disk.Drop($@"D:\Desktop\{name}.lnk").Outcome);
        Assert.Equal(DropOutcome.BrokenShortcut, disk.Drop(@"D:\Desktop\Unreadable.lnk").Outcome);          // the shell could not read it
    }

    [Fact]
    public void A_folder_picks_the_executable_that_matches_its_name_and_skips_installers_and_crash_handlers()
    {
        var disk = new Disk();
        disk.Folders.Add(@"D:\Games\Hades");
        foreach (var exe in new[] { "UnityCrashHandler64.exe", "unins000.exe", "vcredist_x64.exe", "Hades.exe", "Launcher.exe", "EasyAntiCheat_Setup.exe" })
            disk.Files.Add($@"D:\Games\Hades\{exe}");
        var result = disk.Drop(@"D:\Games\Hades");
        Assert.Equal(DropOutcome.Accepted, result.Outcome);
        Assert.Equal(@"D:\Games\Hades\Hades.exe", result.ExecutablePath);
        Assert.Equal([@"D:\Games\Hades\Launcher.exe"], result.Others);
    }

    [Fact]
    public void A_folder_looks_one_level_down_and_a_name_match_beats_a_shallower_unrelated_exe()
    {
        var disk = new Disk();
        disk.Folders.Add(@"D:\Games\My Game");
        disk.Folders.Add(@"D:\Games\My Game\Binaries");
        disk.Files.Add(@"D:\Games\My Game\start.exe");
        disk.Files.Add(@"D:\Games\My Game\Binaries\MyGame.exe");
        disk.Files.Add(@"D:\Games\My Game\Binaries\MyGame-Shipping-Win64.exe");
        var result = disk.Drop(@"D:\Games\My Game");
        Assert.Equal(@"D:\Games\My Game\Binaries\MyGame.exe", result.ExecutablePath);
        Assert.Contains(@"D:\Games\My Game\start.exe", result.Others);
        Assert.Equal(2, result.Others.Count);
    }

    [Fact]
    public void A_folder_with_no_likely_game_unreadable_or_empty_says_so_and_a_duplicate_inside_is_recognised()
    {
        var disk = new Disk();
        disk.Folders.Add(@"D:\Tools\Only helpers");
        disk.Files.Add(@"D:\Tools\Only helpers\setup.exe");
        disk.Files.Add(@"D:\Tools\Only helpers\unins000.exe");
        Assert.Equal(DropOutcome.NoExecutableFound, disk.Drop(@"D:\Tools\Only helpers").Outcome);
        disk.Folders.Add(@"D:\Empty");
        Assert.Equal(DropOutcome.NoExecutableFound, disk.Drop(@"D:\Empty").Outcome);
        disk.Folders.Add(@"D:\Locked");
        disk.Unreadable.Add(@"D:\Locked");
        Assert.Equal(DropOutcome.NoExecutableFound, disk.Drop(@"D:\Locked").Outcome);                       // no crash on an unreadable folder
        disk.Folders.Add(@"D:\Games\Hades");
        disk.Files.Add(Hades);
        disk.Library.Add(Hades);
        Assert.Equal(DropOutcome.AlreadyInLibrary, disk.Drop(@"D:\Games\Hades").Outcome);
        Assert.Equal(DropOutcome.Unsupported, disk.Drop(@"D:\Not\A\Folder").Outcome);
    }

    [Fact]
    public void Only_a_few_other_candidates_are_offered_and_the_choice_does_not_depend_on_listing_order()
    {
        var names = Enumerable.Range(1, 10).Select(i => $"Tool{i:00}.exe").Append("Pack.exe").ToArray();
        DropResolution Run(IEnumerable<string> order)
        {
            var disk = new Disk();
            disk.Folders.Add(@"D:\Games\Pack");
            foreach (var name in order) disk.Files.Add($@"D:\Games\Pack\{name}");
            return disk.Drop(@"D:\Games\Pack");
        }
        var forward = Run(names);
        var reversed = Run(names.Reverse());
        Assert.Equal(@"D:\Games\Pack\Pack.exe", forward.ExecutablePath);
        Assert.Equal(DroppedGameResolver.MaxOtherCandidates, forward.Others.Count);
        Assert.Equal(forward.ExecutablePath, reversed.ExecutablePath);
        Assert.Equal(forward.Others, reversed.Others);
    }
}
