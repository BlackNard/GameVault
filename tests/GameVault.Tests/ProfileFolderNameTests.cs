using GameVault.Core;
using GameVault.Infrastructure.Storage;

namespace GameVault.Tests;

public sealed class ProfileFolderNameTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "GameVault folder names", Guid.NewGuid().ToString("N"));
    private readonly ApplicationPaths paths;

    public ProfileFolderNameTests()
    {
        Directory.CreateDirectory(root);
        paths = ApplicationPaths.FromRoot(root);
    }

    private static GameProfile Profile(string name, Guid? id = null) => new(1, id ?? Guid.NewGuid(), name,
        @"D:\Games\Game\game.exe", @"D:\Games\Game", [], GameStatus.Playing, 20, false, false);
    private string FolderName(Guid id) => Path.GetFileName(ProfileFolders.Resolve(paths, id));

    [Theory]
    [InlineData("TEKKEN 8", "TEKKEN 8")]
    [InlineData("Resident Evil 2: Remake", "Resident Evil 2 Remake")]
    [InlineData("  What? A/B\\C*|<>\"  ", "What A B C")]
    [InlineData("Trailing dots...", "Trailing dots")]
    [InlineData("", "Game")]
    [InlineData("...", "Game")]
    [InlineData("CON", "Game CON")]
    [InlineData("nul.txt", "Game nul.txt")]
    [InlineData("4b8547bf6fea456fb7e30d06955c9a37", "Game 4b8547bf6fea456fb7e30d06955c9a37")]
    [InlineData("Ünïcode 日本語 ゲーム", "Ünïcode 日本語 ゲーム")]
    public void Names_are_windows_safe(string input, string expected)
    {
        var safe = ProfileFolders.SafeName(input);
        Assert.Equal(expected, safe);
        Assert.True(safe.IndexOfAny(Path.GetInvalidFileNameChars()) < 0);
    }

    [Fact]
    public void Very_long_names_are_shortened_without_trailing_spaces()
    {
        var safe = ProfileFolders.SafeName(new string('a', 70) + " " + new string('b', 70));
        Assert.True(safe.Length <= 80);
        Assert.Equal(safe.TrimEnd(), safe);
    }

    [Fact]
    public async Task New_games_get_a_folder_named_after_them_and_duplicates_get_a_counter()
    {
        var store = new AppDataStore(paths);
        await store.InitializeAsync();
        var first = Profile("TEKKEN 8"); var second = Profile("tekken 8"); var third = Profile("TEKKEN 8");
        await store.SaveProfileAsync(first); await store.SaveProfileAsync(second); await store.SaveProfileAsync(third);
        Assert.Equal("TEKKEN 8", FolderName(first.Id));
        Assert.Equal("tekken 8 (2)", FolderName(second.Id));
        Assert.Equal("TEKKEN 8 (3)", FolderName(third.Id));
        var listed = (await new AppDataStore(paths).InitializeAsync()).Games.Select(g => g.Id).ToHashSet();
        Assert.Equal(new[] { first.Id, second.Id, third.Id }.ToHashSet(), listed);
    }

    [Fact]
    public async Task Renaming_a_game_renames_its_folder_and_a_taken_name_gets_a_counter()
    {
        var store = new AppDataStore(paths);
        await store.InitializeAsync();
        var a = Profile("Alpha"); var b = Profile("Beta");
        await store.SaveProfileAsync(a); await store.SaveProfileAsync(b);
        await store.SaveProfileAsync(b with { Name = "Alpha" });
        Assert.Equal("Alpha", FolderName(a.Id));
        Assert.Equal("Alpha (2)", FolderName(b.Id));
        await store.SaveProfileAsync(a with { Name = "Gamma" });
        Assert.Equal("Gamma", FolderName(a.Id));
        // Same name again keeps the existing counter instead of churning.
        await store.SaveProfileAsync(b with { Name = "Alpha" });
        Assert.Equal("Alpha (2)", FolderName(b.Id));
    }

    [Fact]
    public async Task Guid_named_folders_are_renamed_once_keeping_every_file_and_the_stable_id()
    {
        var store = new AppDataStore(paths);
        await store.InitializeAsync();
        var profile = Profile("TEKKEN 8");
        await store.SaveProfileAsync(profile);
        var statistics = await File.ReadAllBytesAsync(Path.Combine(ProfileFolders.Resolve(paths, profile.Id), "statistics.json"));
        // Recreate the old layout: GUID folder, no marker file.
        var named = ProfileFolders.Resolve(paths, profile.Id);
        var legacy = ProfileFolders.Legacy(paths, profile.Id);
        Directory.Move(named, legacy);
        File.Delete(Path.Combine(legacy, ".gamevault-id"));
        File.WriteAllText(Path.Combine(legacy, "cover.png"), "cover");
        Directory.CreateDirectory(Path.Combine(legacy, "Media", "session"));
        File.WriteAllText(Path.Combine(legacy, "Media", "session", "clip.mp4"), "clip");

        var snapshot = await new AppDataStore(paths).InitializeAsync();
        var game = Assert.Single(snapshot.Games);
        Assert.Equal(profile.Id, game.Id);
        Assert.False(Directory.Exists(legacy));
        var folder = Path.Combine(paths.Profiles, "TEKKEN 8");
        Assert.Equal(statistics, await File.ReadAllBytesAsync(Path.Combine(folder, "statistics.json")));
        Assert.Equal("cover", await File.ReadAllTextAsync(Path.Combine(folder, "cover.png")));
        Assert.Equal("clip", await File.ReadAllTextAsync(Path.Combine(folder, "Media", "session", "clip.mp4")));
        Assert.Equal(profile.Id, Assert.Single(snapshot.Games).Id);

        // Idempotent: a second start changes nothing and does not warn.
        var again = await new AppDataStore(paths).InitializeAsync();
        Assert.Equal(["TEKKEN 8"], Directory.GetDirectories(paths.Profiles).Select(p => Path.GetFileName(p)!).ToArray());
        Assert.Equal(profile.Id, Assert.Single(again.Games).Id);
        Assert.Empty(again.Warnings);
    }

    [Fact]
    public async Task A_folder_in_use_is_left_alone_and_renamed_on_a_later_start()
    {
        var store = new AppDataStore(paths);
        await store.InitializeAsync();
        var profile = Profile("Locked game");
        await store.SaveProfileAsync(profile);
        var named = ProfileFolders.Resolve(paths, profile.Id);
        var legacy = ProfileFolders.Legacy(paths, profile.Id);
        Directory.Move(named, legacy);
        using (new FileStream(Path.Combine(legacy, "statistics.json"), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        {
            var snapshot = await new AppDataStore(paths).InitializeAsync();
            Assert.Equal(profile.Id, Assert.Single(snapshot.Games).Id);
            Assert.True(Directory.Exists(legacy)); // could not be renamed while a file inside is open
            Assert.Equal(legacy, ProfileFolders.Resolve(paths, profile.Id));
        }
        await new AppDataStore(paths).InitializeAsync();
        Assert.False(Directory.Exists(legacy));
        Assert.Equal("Locked game", FolderName(profile.Id));
    }

    [Fact]
    public async Task A_damaged_profile_json_does_not_hide_the_game_because_the_id_is_also_kept_in_a_marker()
    {
        var store = new AppDataStore(paths);
        await store.InitializeAsync();
        var profile = Profile("Damaged");
        await store.SaveProfileAsync(profile);
        var folder = ProfileFolders.Resolve(paths, profile.Id);
        File.WriteAllText(Path.Combine(folder, "profile.json"), "not json");
        File.Delete(Path.Combine(folder, "profile.json.bak"));
        var snapshot = await new AppDataStore(paths).InitializeAsync();
        var game = Assert.Single(snapshot.Games);
        Assert.Equal(profile.Id, game.Id);
        Assert.NotEqual(DocumentState.Ready, game.Profile.State);
        Assert.Equal("Damaged", Path.GetFileName(folder)); // never renamed from unreadable data
        Assert.True(Directory.Exists(folder));
    }

    [Fact]
    public async Task A_name_based_folder_without_a_marker_is_identified_from_profile_json()
    {
        var store = new AppDataStore(paths);
        await store.InitializeAsync();
        var profile = Profile("No marker");
        await store.SaveProfileAsync(profile);
        File.Delete(Path.Combine(ProfileFolders.Resolve(paths, profile.Id), ".gamevault-id"));
        var snapshot = await new AppDataStore(paths).InitializeAsync();
        Assert.Equal(profile.Id, Assert.Single(snapshot.Games).Id);
    }

    [Fact]
    public async Task Unidentifiable_and_repeated_ids_are_reported_and_left_unchanged()
    {
        var store = new AppDataStore(paths);
        await store.InitializeAsync();
        var profile = Profile("Real");
        await store.SaveProfileAsync(profile);
        Directory.CreateDirectory(Path.Combine(paths.Profiles, "Some other folder"));
        var copy = Path.Combine(paths.Profiles, "Zzz copy");
        Directory.CreateDirectory(copy);
        File.Copy(Path.Combine(ProfileFolders.Resolve(paths, profile.Id), "profile.json"), Path.Combine(copy, "profile.json"));
        var snapshot = await new AppDataStore(paths).InitializeAsync();
        Assert.Equal(profile.Id, Assert.Single(snapshot.Games).Id);
        Assert.Equal(2, snapshot.Warnings.Count(w => w.Contains("unrecognized profile folder")));
        Assert.True(Directory.Exists(Path.Combine(paths.Profiles, "Some other folder")));
        Assert.True(Directory.Exists(copy));
    }

    public void Dispose() => Directory.Delete(root, recursive: true);
}
