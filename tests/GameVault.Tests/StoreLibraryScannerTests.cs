using GameVault.Infrastructure.Launching;
using GameVault.Infrastructure.Library;

namespace GameVault.Tests;

public sealed class StoreLibraryScannerTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "gv-store-" + Guid.NewGuid().ToString("N"));
    private readonly string steam, library2, epic;

    public StoreLibraryScannerTests()
    {
        steam = Path.Combine(root, "Steam");
        library2 = Path.Combine(root, "Games", "SteamLibrary");
        epic = Path.Combine(root, "Epic", "Manifests");
        Directory.CreateDirectory(Path.Combine(steam, "steamapps", "common"));
        Directory.CreateDirectory(Path.Combine(library2, "steamapps", "common"));
        Directory.CreateDirectory(epic);
        File.WriteAllText(Path.Combine(steam, "steamapps", "libraryfolders.vdf"),
            "\"libraryfolders\"\n{\n\t\"0\"\n\t{\n\t\t\"path\"\t\t\"" + steam.Replace("\\", "\\\\") + "\"\n\t}\n\t\"1\"\n\t{\n\t\t\"path\"\t\t\"" + library2.Replace("\\", "\\\\") + "\"\n\t}\n}\n");
    }

    public void Dispose() => Directory.Delete(root, true);

    private static void Exe(string folder, string name, int size = 10)
    {
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, name), new byte[size]);
    }

    private void SteamGame(string library, string id, string name, string install, string flags = "4", string size = "123456")
    {
        File.WriteAllText(Path.Combine(library, "steamapps", $"appmanifest_{id}.acf"),
            $"\"AppState\"\n{{\n\t\"appid\"\t\t\"{id}\"\n\t\"name\"\t\t\"{name}\"\n\t\"StateFlags\"\t\t\"{flags}\"\n\t\"installdir\"\t\t\"{install}\"\n\t\"SizeOnDisk\"\t\t\"{size}\"\n\t\"UserConfig\"\n\t{{\n\t\t\"language\"\t\t\"english\"\n\t}}\n}}\n");
    }

    private StoreScanResult Scan() => new StoreLibraryScanner(steam, epic).Scan();

    [Fact]
    public void Games_from_every_steam_library_are_listed_with_size_and_executable()
    {
        SteamGame(steam, "1245620", "ELDEN RING", "ELDEN RING");
        Exe(Path.Combine(steam, "steamapps", "common", "ELDEN RING", "Game"), "eldenring.exe");
        Exe(Path.Combine(steam, "steamapps", "common", "ELDEN RING"), "start_protected_game.exe", 5);
        SteamGame(library2, "620", "Portal 2", "Portal 2");
        Exe(Path.Combine(library2, "steamapps", "common", "Portal 2"), "portal2.exe", 50);
        Exe(Path.Combine(library2, "steamapps", "common", "Portal 2"), "unins000.exe", 90);
        var result = Scan();
        Assert.Equal(new[] { "ELDEN RING", "Portal 2" }, result.Games.Select(g => g.Name));
        var portal = result.Games.Single(g => g.StoreId == "620");
        Assert.Equal(Path.Combine(library2, "steamapps", "common", "Portal 2", "portal2.exe"), portal.ExecutablePath);
        Assert.Equal(123456, portal.SizeBytes);
        Assert.DoesNotContain(portal.Candidates, c => c.EndsWith("unins000.exe"));
        Assert.Equal(StoreProvider.Steam, portal.Provider);
    }

    [Fact]
    public void Redistributables_tools_and_unfinished_installs_are_filtered_out()
    {
        SteamGame(steam, "228980", "Steamworks Common Redistributables", "Steamworks Shared");
        SteamGame(steam, "1493710", "Proton Experimental", "Proton - Experimental");
        SteamGame(steam, "999", "Some Redistributable Pack", "Redist");
        SteamGame(steam, "55", "Half Installed", "Half", flags: "2");
        SteamGame(steam, "56", "Updating Game", "Upd", flags: "1030");
        foreach (var folder in new[] { "Steamworks Shared", "Proton - Experimental", "Redist", "Half", "Upd" }) Exe(Path.Combine(steam, "steamapps", "common", folder), "a.exe");
        SteamGame(steam, "57", "Fully Installed", "Full");
        Exe(Path.Combine(steam, "steamapps", "common", "Full"), "full.exe");
        File.WriteAllText(Path.Combine(epic, "tool.item"), "{\"AppName\":\"UE\",\"DisplayName\":\"Unreal Engine Redistributable\",\"InstallLocation\":\"" + root.Replace("\\", "\\\\") + "\"}");
        // 1030 = installed (4) while an update runs: the game is on disk, so it is listed. 2 = only partly downloaded: not listed.
        Assert.Equal(new[] { "Fully Installed", "Updating Game" }, Scan().Games.Select(g => g.Name));
        Assert.True(StoreLibraryScanner.IsToolName("Steam Linux Runtime 3.0 (sniper)"));
        Assert.False(StoreLibraryScanner.IsToolName("Redfall"));
    }

    [Fact]
    public void Malformed_or_oversized_or_unsafe_manifests_are_skipped_with_a_warning_and_never_stop_the_scan()
    {
        SteamGame(steam, "10", "Good", "Good");
        Exe(Path.Combine(steam, "steamapps", "common", "Good"), "good.exe");
        File.WriteAllText(Path.Combine(steam, "steamapps", "appmanifest_11.acf"), "\"AppState\" { \"appid\" ");
        File.WriteAllText(Path.Combine(steam, "steamapps", "appmanifest_12.acf"), new string('x', 600 * 1024));
        SteamGame(steam, "13", "Traversal", "..\\..\\Windows");
        File.WriteAllText(Path.Combine(steam, "steamapps", "appmanifest_14.acf"), "\"AppState\"\n{\n\"appid\" \"15\"\n\"name\" \"Mismatch\"\n\"installdir\" \"Good\"\n}\n");
        File.WriteAllText(Path.Combine(epic, "bad.item"), "{ not json");
        var result = Scan();
        Assert.Equal(new[] { "Good" }, result.Games.Select(g => g.Name));
        Assert.NotEmpty(result.Warnings);
    }

    [Fact]
    public void Epic_games_use_the_launch_executable_and_skip_dlc_and_incomplete_installs()
    {
        var install = Path.Combine(root, "Epic Games", "Fortnite");
        Exe(Path.Combine(install, "Binaries"), "launch.exe", 1);
        Exe(install, "other.exe", 1000);
        var json = (string app, string name, string main, bool incomplete) => "{\"AppName\":\"" + app + "\",\"DisplayName\":\"" + name + "\",\"MainGameAppName\":\"" + main + "\",\"InstallLocation\":\"" + install.Replace("\\", "\\\\") + "\",\"LaunchExecutable\":\"Binaries\\\\launch.exe\",\"InstallSize\":777,\"bIsIncompleteInstall\":" + (incomplete ? "true" : "false") + "}";
        File.WriteAllText(Path.Combine(epic, "a.item"), json("Main", "Fortnite", "Main", false));
        File.WriteAllText(Path.Combine(epic, "b.item"), json("Dlc", "Fortnite Skin Pack", "Main", false));
        File.WriteAllText(Path.Combine(epic, "c.item"), json("Half", "Half Installed", "Half", true));
        var games = Scan().Games;
        var game = Assert.Single(games);
        Assert.Equal(("Main", StoreProvider.Epic, 777L), (game.StoreId, game.Provider, game.SizeBytes));
        Assert.Equal(Path.Combine(install, "Binaries", "launch.exe"), game.ExecutablePath);
    }

    [Fact]
    public void Unreal_shipping_executables_are_found_and_the_best_name_match_wins()
    {
        var install = Path.Combine(root, "Unreal Game");
        Exe(Path.Combine(install, "Project", "Binaries", "Win64"), "Project-Win64-Shipping.exe", 500);
        Exe(install, "UE4PrereqSetup_x64.exe", 900);
        var (best, candidates) = StoreLibraryScanner.Executables(install, "Project", "Unreal Game");
        Assert.EndsWith("Project-Win64-Shipping.exe", best);
        Assert.Single(candidates);

        var many = Path.Combine(root, "Many");
        Exe(many, "launcher.exe", 900);
        Exe(many, "Cool Game.exe", 100);
        Exe(many, "tool.exe", 800);
        Assert.EndsWith("Cool Game.exe", StoreLibraryScanner.Executables(many, "Cool Game", "Many").Best);
        Assert.Null(StoreLibraryScanner.Executables(Path.Combine(root, "Empty"), "x", "y").Best);
    }

    [Fact]
    public void Games_already_in_the_library_are_recognised_and_removed_ones_offer_restore()
    {
        var install = Path.Combine(root, "Games", "Foo");
        var game = new InstalledStoreGame(StoreProvider.Steam, "1", "Foo", install, Path.Combine(install, "foo.exe"), [], null);
        var id = Guid.NewGuid();
        Assert.Equal((StoreGameStatus.New, (Guid?)null), StoreLibraryScanner.Classify(game, []));
        Assert.Equal((StoreGameStatus.InLibrary, (Guid?)id), StoreLibraryScanner.Classify(game, [new(id, Path.Combine(install, "bin", "Other.exe"), false)]));
        Assert.Equal((StoreGameStatus.Removed, (Guid?)id), StoreLibraryScanner.Classify(game, [new(id, Path.Combine(install, "foo.exe"), true)]));
        Assert.Equal(StoreGameStatus.New, StoreLibraryScanner.Classify(game, [new(Guid.NewGuid(), Path.Combine(root, "Games", "Foobar", "foo.exe"), false)]).Status);
    }

    [Fact]
    public void Missing_steam_and_epic_installations_give_an_empty_result()
    {
        var result = new StoreLibraryScanner(null, Path.Combine(root, "nothing")).Scan();
        Assert.Empty(result.Games);
        Assert.Empty(new StoreLibraryScanner(Path.Combine(root, "nope"), Path.Combine(root, "nothing")).Scan().Games);
    }
}
