using GameVault.Core;
using GameVault.Infrastructure.Library;
using GameVault.Infrastructure.Storage;

namespace GameVault.Tests;

/// <summary>CSV export (ticket #98), on a synthetic library.</summary>
public sealed class CsvExportTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "GameVault csv", Guid.NewGuid().ToString("N"));
    private readonly ApplicationPaths paths;
    private readonly AppDataStore store;
    private readonly LibraryService library;

    public CsvExportTests()
    {
        paths = ApplicationPaths.FromRoot(Path.Combine(root, "app"));
        Directory.CreateDirectory(paths.Root);
        Directory.CreateDirectory(Path.Combine(root, "out"));
        store = new(paths);
        library = new(paths, store);
    }

    public void Dispose() { try { Directory.Delete(root, true); } catch (IOException) { } }

    [Theory]
    [InlineData("plain", "plain")]
    [InlineData("a,b", "\"a,b\"")]
    [InlineData("say \"hi\"", "\"say \"\"hi\"\"\"")]
    [InlineData("two\nlines", "\"two\nlines\"")]
    [InlineData("=SUM(A1)", "'=SUM(A1)")]
    [InlineData("+1", "'+1")]
    [InlineData("-x", "'-x")]
    [InlineData("@x", "'@x")]
    [InlineData("\t=x", "'\t=x")]
    [InlineData(" =x", "' =x")]
    [InlineData("=a,b", "\"'=a,b\"")]
    public void CellsAreQuotedAndNeverRunAsFormulas(string value, string expected) => Assert.Equal(expected, Csv.Cell(value));

    [Fact]
    public async Task TheLibraryIsWrittenToSeparateFilesWithoutPathsAndNothingIsOverwritten()
    {
        var folder = Path.Combine(root, "secret-folder");
        Directory.CreateDirectory(folder);
        var exe = Path.Combine(folder, "game.exe");
        File.WriteAllText(exe, "fixture only; never executed");
        var id = (await library.SaveAsync(new(null, "Alpha, the game", exe, folder, [], GameStatus.OnHold, Detailed: PlayStatus.Retired))).Profile.Id;
        var at = new DateTimeOffset(2026, 5, 3, 20, 0, 0, TimeSpan.Zero);
        await store.SaveStatisticsAsync(new(1, id, [new(Guid.NewGuid(), at, at.AddHours(1), 3600)], []));
        await store.SaveRatingAsync(id, 7);
        await store.SavePlaythroughAsync(id, new(Guid.NewGuid(), "First", "PC", new(2026, 5, 1), null, PlaythroughOutcome.Playing, 90, 0, null, true));
        await store.SaveJournalEntryAsync(id, new(Guid.NewGuid(), new(2026, 5, 4), 30, "=danger", ["a", "b"], null));
        await store.ChangeListsAsync(d => GameListRules.ToggleFavourite(GameListRules.Upsert(d, new(Guid.NewGuid(), "Best", "", true, [new(Guid.NewGuid(), id, "Alpha", null)])), id));

        var service = new CsvExportService(store);
        var written = await service.ExportAsync(Path.Combine(root, "out"), TimeZoneInfo.Utc, new DateTime(2026, 5, 5, 9, 0, 0));
        Assert.Equal(["games.csv", "journal.csv", "lists.csv", "playthroughs.csv", "sessions.csv"], Directory.GetFiles(written.Folder).Select(Path.GetFileName).Order());
        var games = File.ReadAllText(Path.Combine(written.Folder, "games.csv"));
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, File.ReadAllBytes(Path.Combine(written.Folder, "games.csv")).Take(3));
        Assert.StartsWith("Name,Status,Rating,Hours played,Sessions,Last played,Completed on,Favourite,Tags,Genres,Removed\r\n", games);
        Assert.Contains("\"Alpha, the game\",Retired,3.5,1.00,1,2026-05-03,,yes,,,no", games);
        Assert.Contains("\"Alpha, the game\",2026-05-03 20:00,2026-05-03 21:00,60", File.ReadAllText(Path.Combine(written.Folder, "sessions.csv")));
        Assert.Contains("First,PC,2026-05-01,,Playing,1.50,,yes,", File.ReadAllText(Path.Combine(written.Folder, "playthroughs.csv")));
        Assert.Contains("2026-05-04,30,'=danger,a; b", File.ReadAllText(Path.Combine(written.Folder, "journal.csv")));
        Assert.Contains("Best,yes,1,\"Alpha, the game\",", File.ReadAllText(Path.Combine(written.Folder, "lists.csv")));
        Assert.All(Directory.GetFiles(written.Folder), f => Assert.DoesNotContain("secret-folder", File.ReadAllText(f)));
        var again = await service.ExportAsync(Path.Combine(root, "out"), TimeZoneInfo.Utc, new DateTime(2026, 5, 5, 9, 0, 0));
        Assert.NotEqual(written.Folder, again.Folder);
        await Assert.ThrowsAsync<IOException>(() => service.ExportAsync(paths.Data, TimeZoneInfo.Utc, DateTime.Now));
    }
}
