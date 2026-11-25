using System.Text.Json;
using GameVault.Core;
using GameVault.Infrastructure.Capture;
using GameVault.Infrastructure.Library;
using GameVault.Infrastructure.Storage;

namespace GameVault.Tests;

public sealed class MediaFolderTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "GameVault media folders", Guid.NewGuid().ToString("N"));
    private readonly ApplicationPaths paths;
    private readonly LibraryService library;
    private readonly CaptureMediaStore store;

    public MediaFolderTests()
    {
        Directory.CreateDirectory(Path.Combine(root, "game"));
        paths = ApplicationPaths.FromRoot(root);
        library = new(paths, new AppDataStore(paths));
        store = new(paths);
    }

    public void Dispose()
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(root, true);
        }
        catch (IOException) { }
    }

    private async Task<Guid> AddGameAsync()
    {
        var exe = Path.Combine(root, "game", "game.exe");
        File.WriteAllText(exe, "fixture only; never executed");
        return (await library.SaveAsync(new(null, "Captured", exe, "", []))).Profile.Id;
    }

    /// <summary>A finished screenshot in the old layout, written the way earlier versions did.</summary>
    private string Legacy(Guid game, Guid session, DateTimeOffset when)
    {
        var folder = Path.Combine(ProfileFolders.Resolve(paths, game), "Media", session.ToString("N"));
        Directory.CreateDirectory(folder);
        var id = Guid.NewGuid();
        var name = $"{when.ToUniversalTime():yyyyMMddTHHmmssfffffffZ}-{id:N}.png";
        File.WriteAllText(Path.Combine(folder, name), "picture " + id);
        File.WriteAllText(Path.Combine(folder, name + ".json"), JsonSerializer.Serialize(new
        {
            Id = id, ProfileId = game, SessionId = session, CapturedUtc = when.ToUniversalTime(), Kind = "Screenshot", Status = "Complete", FilePath = name
        }));
        return name;
    }

    [Fact]
    public async Task OldFoldersGetDateNamesAndEveryCaptureIsStillThere()
    {
        var game = await AddGameAsync();
        var when = new DateTimeOffset(2026, 11, 18, 19, 45, 0, TimeSpan.Zero);
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var names = new[] { Legacy(game, first, when), Legacy(game, first, when.AddMinutes(3)), Legacy(game, second, when.AddSeconds(20)) };
        var before = await store.ListAsync(game);
        Assert.Equal(3, before.Length);

        Assert.Equal(2, MediaFolders.MigrateAll(paths));
        Assert.Equal(0, MediaFolders.MigrateAll(paths));

        var media = Path.Combine(ProfileFolders.Resolve(paths, game), "Media");
        var expected = MediaFolders.NameFor(when);
        Assert.Equal([expected, expected + " (2)"], Directory.GetDirectories(media).Select(Path.GetFileName).Order(StringComparer.Ordinal));
        var after = await store.ListAsync(game);
        Assert.Equal(before.Select(m => m.Id).Order(), after.Select(m => m.Id).Order());
        Assert.All(after, m => Assert.True(File.Exists(m.FilePath)));
        Assert.Equal(names.Order(), after.Select(m => Path.GetFileName(m.FilePath)).Order());
    }

    [Fact]
    public async Task OneSessionKeepsOneFolderAndANewOneIsNamedByDate()
    {
        var game = await AddGameAsync();
        var session = Guid.NewGuid();
        var when = new DateTimeOffset(2026, 1, 2, 3, 4, 0, TimeSpan.Zero);
        var one = await store.BeginImportAsync(game, session, CaptureMediaKind.Screenshot, when, "a");
        var two = await store.BeginImportAsync(game, session, CaptureMediaKind.Video, when.AddHours(2), "b");
        Assert.Equal(Path.GetDirectoryName(one.FilePath), Path.GetDirectoryName(two.FilePath));
        Assert.Equal(MediaFolders.NameFor(when), Path.GetFileName(Path.GetDirectoryName(one.FilePath)));
        Assert.Equal(session, MediaFolders.Identify(Path.GetDirectoryName(one.FilePath)!));
        Assert.Equal(2, (await store.ListAsync(game)).Length);
        store.Discard(two);
        Assert.Single(await store.ListAsync(game));
    }

    [Fact]
    public async Task AFolderThatDoesNotBelongToASessionIsIgnoredAndLeftAlone()
    {
        var game = await AddGameAsync();
        var mine = Path.Combine(ProfileFolders.Resolve(paths, game), "Media", "my own pictures");
        Directory.CreateDirectory(mine);
        File.WriteAllText(Path.Combine(mine, "holiday.png"), "x");
        var empty = Path.Combine(ProfileFolders.Resolve(paths, game), "Media", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(empty);
        Assert.Equal(0, MediaFolders.MigrateAll(paths));
        Assert.True(File.Exists(Path.Combine(mine, "holiday.png")));
        Assert.True(Directory.Exists(empty));
        Assert.Empty(await store.ListAsync(game));
    }
}
