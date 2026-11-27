using System.Net;
using System.Text;
using GameVault.Core;
using GameVault.Infrastructure.Metadata;
using GameVault.Infrastructure.Storage;

namespace GameVault.Tests;

/// <summary>
/// Names written differently from the store's (ticket #105). "Silent Hill 2 Remake" found nothing because Steam calls the game "SILENT HILL 2"; the
/// answers below are what Steam's search really returned for these queries.
/// </summary>
public sealed class GameTitlesTests
{
    [Theory]
    [InlineData("Grand Theft Auto IV - The Complete Edition", "Grand Theft Auto IV: The Complete Edition", 0)]
    [InlineData("Grand Theft Auto 4 Complete Edition", "Grand Theft Auto IV: The Complete Edition", 1)]
    [InlineData("GTA 4", "Grand Theft Auto IV: The Complete Edition", 3)]
    [InlineData("Assassin's Creed II", "Assassins Creed 2", 1)]
    [InlineData("The Witcher 3", "Witcher 3", 1)]
    [InlineData("Remake Silent Hill 2", "SILENT HILL 2 Remake", 2)]
    [InlineData("Final Fantasy 7 Remake", "FINAL FANTASY VII REMAKE INTERGRADE", 3)]
    [InlineData("Silent Hill 2 Remake", "SILENT HILL 2", 4)]
    [InlineData("Metro Exodus Gold Edition", "Metro Exodus Enhanced Edition", 5)]
    public void TheSameGameWrittenDifferentlyFits(string wanted, string found, int fit) => Assert.Equal(fit, GameTitles.Fit(wanted, found));

    [Theory]
    [InlineData("Silent Hill 2", "SILENT HILL 3")]
    [InlineData("Silent Hill 2", "SILENT HILL f")]
    [InlineData("Tekken", "TEKKEN 8")]
    [InlineData("Silent Hill 2 Remake", "FATAL FRAME II: Crimson Butterfly REMAKE x SILENT HILL f Costume Set")]
    [InlineData("Final Fantasy 7 Remake", "FINAL FANTASY VII REMAKE Original Soundtrack")]
    [InlineData("Grand Theft Auto IV - Episodes", "Grand Theft Auto IV: The Complete Edition")]
    [InlineData("Hades", "Hades II")]
    [InlineData("Resident Evil 4", "Resident Evil 4 Separate Ways")]
    [InlineData("", "Anything")]
    public void AnotherGameNeverFits(string wanted, string found) => Assert.Null(GameTitles.Fit(wanted, found));

    [Fact]
    public void TheBestFitWinsAndEqualAnswersAreNotGuessed()
    {
        var (match, fit, ambiguous) = GameTitles.Best("Dead Space Remake", ["Dead Space 2", "Dead Space"], s => s);
        Assert.Equal(("Dead Space", 4, false), (match, fit, ambiguous));
        Assert.True(GameTitles.Best("Twin", ["Twin", "TWIN"], s => s).Ambiguous);
    }

    [Theory]
    [InlineData("Silent Hill 2 Remake", "Silent Hill 2|Silent Hill II")]
    [InlineData("GTA 4", "Grand Theft Auto 4|Grand Theft Auto IV|Grand Theft Auto|Grand Theft")]
    [InlineData("Tekken 8", "")]
    [InlineData("Grand Theft Auto IV", "Grand Theft Auto 4|Grand Theft Auto|Grand Theft")]
    [InlineData("Alan Wake 2 Deluxe Edition", "Alan Wake 2|Alan Wake II")]
    public void OtherWaysOfAskingForATitle(string typed, string expected) => Assert.Equal(expected, string.Join('|', GameTitles.Fallbacks(typed)));

    private sealed class Steam : HttpMessageHandler
    {
        public readonly List<string> Terms = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var query = request.RequestUri!.Query;
            var term = Uri.UnescapeDataString(query[(query.IndexOf("term=", StringComparison.Ordinal) + 5)..].Split('&')[0]);
            Terms.Add(term);
            (int Id, string Name)[] apps = term.ToLowerInvariant() switch
            {
                "silent hill 2 remake" => [(4423570, "FATAL FRAME II: Crimson Butterfly REMAKE x SILENT HILL f Costume Set")],
                "silent hill 2" => [(2124490, "SILENT HILL 2"), (4423570, "FATAL FRAME II: Crimson Butterfly REMAKE x SILENT HILL f Costume Set")],
                "grand theft auto 4" => [(12210, "Grand Theft Auto IV: The Complete Edition")],
                _ => []
            };
            var json = "{\"items\":[" + string.Join(",", apps.Select(a => $"{{\"type\":\"app\",\"name\":\"{a.Name}\",\"id\":{a.Id}}}")) + "]}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
        }
    }

    [Theory]
    [InlineData("Silent Hill 2 Remake", "2124490")]
    [InlineData("GTA 4", "12210")]
    public async Task SteamIsAskedAgainInAnotherWayUntilTheGameIsFound(string typed, string appId)
    {
        var handler = new Steam();
        using var provider = new SteamStoreProvider(handler, minimumGap: TimeSpan.Zero);
        Assert.Equal(appId, await provider.FindAppIdAsync(typed, CancellationToken.None));
        Assert.InRange(handler.Terms.Count, 2, GameTitles.MaxQueries);
        var offered = await provider.SearchAsync(typed, CancellationToken.None);
        Assert.Equal(appId, offered[0].Id);                                      // the picker lists the fitting game first
    }

    [Fact]
    public async Task RenamingAListGameKeepsItsPlaceAndForgetsWhatWasFetched()
    {
        var root = Path.Combine(Path.GetTempPath(), "GameVault rename", Guid.NewGuid().ToString("N"));
        try
        {
            var paths = ApplicationPaths.FromRoot(Path.Combine(root, "app"));
            Directory.CreateDirectory(paths.Root);
            var store = new AppDataStore(paths);
            var entry = new ListEntry(Guid.NewGuid(), null, "Silent Hil 2", "note");
            await store.ChangeListsAsync(d => GameListRules.Upsert(d, new(Guid.NewGuid(), "Horror", "", true, [entry])));
            await store.SaveListGameInfoAsync(GameMetadataDocument.Empty(entry.Id));
            byte[] png = [137, 80, 78, 71, 13, 10, 26, 10, .. new byte[40]];
            await store.SaveListGameCoverAsync(entry.Id, png, own: false);
            await store.SaveListGameCoverAsync(entry.Id, png, own: true);
            var changed = await store.ChangeListsAsync(d => GameListRules.Rename(d, entry.Id, " Silent Hill 2 "));
            await store.ForgetListGameAsync(entry.Id);
            var kept = Assert.Single(changed.Lists[0].Entries);
            Assert.Equal((entry.Id, "Silent Hill 2", "note"), (kept.Id, kept.Title, kept.Note));
            Assert.Null((await store.LoadListGameInfoAsync(entry.Id)).Value);
            Assert.EndsWith(".own.png", store.ListGameCover(entry.Id));
        }
        finally { try { Directory.Delete(root, true); } catch (IOException) { } }
    }
}
