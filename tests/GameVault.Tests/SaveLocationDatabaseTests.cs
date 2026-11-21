using System.Net;
using System.Text;
using GameVault.Core;
using GameVault.Infrastructure.Saves;
using GameVault.Infrastructure.Storage;

namespace GameVault.Tests;

public sealed class SaveLocationDatabaseTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "gv-ludusavi-" + Guid.NewGuid().ToString("N"));
    private ApplicationPaths Paths => ApplicationPaths.FromRoot(Path.Combine(root, "app"));
    private SaveDiscoveryEnvironment Env(ISaveLocationSource? database = null) => new(Path.Combine(root, "user"), Path.Combine(root, "user", "Documents"), Path.Combine(root, "user", "Saved Games"),
        Path.Combine(root, "user", "AppData", "Roaming"), Path.Combine(root, "user", "AppData", "Local"), Path.Combine(root, "Public", "Documents"), [Path.Combine(root, "Steam")], database);

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }

    private const string Sample = """
        An Example Game:
          files:
            <base>/saves:
              tags:
                - save
            <winDocuments>/My Example/*/profile.sav:
              tags:
                - save
            <winAppData>/Example/settings.ini:
              tags:
                - config
            <home>/.mac-only:
              when:
                - os: mac
              tags:
                - save
            <storeUserId>/<storeGameId>/remote:
              when:
                - os: windows
                  store: steam
              tags:
                - save
          installDir:
            ExampleGame: {}
          registry:
            HKEY_CURRENT_USER/Software/Example:
              tags:
                - save
          steam:
            id: 4242
        "Quoted: Game's Name":
          files:
            "<winLocalAppData>/Quoted \"Dir\"":
              tags:
                - save
          steam:
            id: 99
        Only Config Game:
          files:
            <base>/cfg:
              tags:
                - config
        Windows Missing:
          files:
            <winPublic>/Documents/Steam/CODEX/<storeGameId>:
              tags:
                - save
          steam:
            id: 883710
        """;

    private static IReadOnlyList<ManifestGame> Parse(string text) { using var reader = new StringReader(text); return LudusaviManifest.Parse(reader); }

    [Fact]
    public void The_manifest_reader_keeps_windows_save_entries_and_ignores_the_rest()
    {
        var games = Parse(Sample);
        Assert.Equal(new[] { "An Example Game", "Quoted: Game's Name", "Windows Missing" }, games.Select(g => g.Name));
        var example = games[0];
        Assert.Equal("4242", example.SteamId);
        Assert.Equal(new[] { "ExampleGame" }, example.InstallDirs);
        Assert.Equal(new[] { "<base>/saves", "<winDocuments>/My Example/*/profile.sav", "<storeUserId>/<storeGameId>/remote" }, example.Files.Select(f => f.Path));
        Assert.Equal(new[] { "steam" }, example.Files[2].Stores);
        Assert.Equal("<winLocalAppData>/Quoted \"Dir\"", games[1].Files.Single().Path);
        Assert.Empty(Parse("garbage line\n  - nothing\n\u0000\n"));
    }

    private ManifestGame Game(string path) => new("Game", "4242", ["Game"], [new(path, ["save"], [], [])]);

    [Fact]
    public void Placeholders_expand_to_the_windows_known_folders_and_each_steam_account()
    {
        Directory.CreateDirectory(Path.Combine(root, "Steam", "userdata", "1001"));
        Directory.CreateDirectory(Path.Combine(root, "Steam", "userdata", "1002"));
        Directory.CreateDirectory(Path.Combine(root, "Steam", "userdata", "notanid"));
        var env = Env();
        var install = Path.Combine(root, "Games", "Cool Game");
        string[] Expand(string t) => [.. SaveLocationDatabase.Expand(t, Game(t), "4242", install, env)];
        Assert.Equal(new[] { Path.Combine(install, "saves") }, Expand("<base>/saves"));
        Assert.Equal(new[] { Path.Combine(root, "user", "Documents", "My Games", "Cool Game") }, Expand("<winDocuments>/My Games/<game>"));
        Assert.Equal(new[] { Path.Combine(root, "user", "AppData", "Roaming", "X"), }, Expand("<winAppData>/X"));
        Assert.Equal(new[] { Path.Combine(root, "user", "AppData", "LocalLow", "X") }, Expand("<winLocalAppDataLow>/X"));
        Assert.Equal(new[] { Path.Combine(root, "Public", "Steam", "4242") }, Expand("<winPublic>/Steam/<storeGameId>"));
        var accounts = Expand("<home>/steam/<storeUserId>/<storeGameId>/remote").Select(p => p.Split(Path.DirectorySeparatorChar)[^3]).Order();
        Assert.Equal(new[] { "1001", "1002" }, accounts);
        Assert.Equal(Path.Combine(root, "Games"), Expand("<root>")[0]);
    }

    [Theory]
    [InlineData("<unknownThing>/x")]
    [InlineData("relative/path")]
    [InlineData("<base>/../escape")]
    [InlineData("<winAppData>/a/./b/../c")]
    [InlineData("")]
    public void Unknown_placeholders_relative_paths_and_traversal_give_nothing(string template)
    {
        Assert.Empty(SaveLocationDatabase.Expand(template, Game(template), "4242", Path.Combine(root, "Games", "G"), Env()));
    }

    [Fact]
    public void Missing_store_ids_and_wildcards_are_handled()
    {
        var env = Env();
        Assert.Empty(SaveLocationDatabase.Expand("<winAppData>/<storeGameId>", Game("x"), null, null, env));      // no Steam id for this game
        Assert.Empty(SaveLocationDatabase.Expand("<storeUserId>/x", Game("x"), "1", null, env));                 // no Steam accounts on this PC
        var docs = Path.Combine(root, "user", "Documents", "My Example");
        Directory.CreateDirectory(Path.Combine(docs, "slotA"));
        Directory.CreateDirectory(Path.Combine(docs, "slotB"));
        var found = SaveLocationDatabase.Expand("<winDocuments>/My Example/*", Game("x"), null, null, env);
        Assert.Equal(new[] { Path.Combine(docs, "slotA"), Path.Combine(docs, "slotB") }, found.Order());
        Assert.Empty(SaveLocationDatabase.Expand("<winDocuments>/Nothing Here/*/x", Game("x"), null, null, env));
    }

    private void WriteManifest(string text)
    {
        var cache = Path.Combine(root, "app", "Cache");
        Directory.CreateDirectory(cache);
        File.WriteAllText(Path.Combine(cache, "ludusavi-manifest.yaml"), text);
    }

    private GameProfile Profile(string name, string folder) => new(1, Guid.NewGuid(), name, Path.Combine(folder, "game.exe"), folder, [], GameStatus.NotStarted, 20, false, false);

    [Fact]
    public void Candidates_come_from_the_offline_copy_by_exact_name_and_only_where_the_folder_exists()
    {
        WriteManifest(Sample);
        var database = new SaveLocationDatabase(Paths);
        Assert.True(database.Status().Available);
        Assert.Equal(3, database.Status().Games);
        var install = Path.Combine(root, "Games", "Whatever");
        var profile = Profile("An Example Game", install);
        Assert.Empty(database.Candidates(profile, Env()));                                                       // nothing exists yet
        Directory.CreateDirectory(Path.Combine(install, "saves"));
        Assert.Equal(new[] { Path.Combine(install, "saves") }, database.Candidates(profile, Env()));
        Assert.Empty(database.Candidates(Profile("An Example Game Deluxe Edition", install), Env()));          // no fuzzy matches
        Assert.Empty(database.Candidates(Profile("Zz", install), Env()));
    }

    [Fact]
    public void Games_are_matched_by_install_folder_name_too()
    {
        WriteManifest(Sample);
        var install = Path.Combine(root, "Games", "ExampleGame");
        Directory.CreateDirectory(Path.Combine(install, "saves"));
        Assert.Single(new SaveLocationDatabase(Paths).Candidates(Profile("Some Renamed Title", install), Env()));
    }

    [Fact]
    public void Without_a_downloaded_copy_there_are_no_candidates_and_nothing_breaks()
    {
        var database = new SaveLocationDatabase(Paths);
        Assert.False(database.Status().Available);
        Assert.Empty(database.Candidates(Profile("An Example Game", Path.Combine(root, "x")), Env()));
    }

    [Fact]
    public async Task Database_suggestions_go_through_the_normal_confirmation_flow_and_are_never_selected_automatically()
    {
        WriteManifest(Sample);
        var install = Path.Combine(root, "Games", "An Example Game");
        Directory.CreateDirectory(install);
        var documents = Path.Combine(root, "user", "Documents", "My Example", "slotA");
        Directory.CreateDirectory(documents);
        File.WriteAllText(Path.Combine(documents, "profile.sav"), "save");
        Directory.CreateDirectory(Paths.Root);
        var store = new AppDataStore(Paths);
        await store.InitializeAsync();
        var profile = Profile("An Example Game", install);
        await store.SaveProfileAsync(profile);
        var service = new SaveDiscoveryService(Paths, store, Env(new SaveLocationDatabase(Paths)));
        var result = await service.ScanAsync(profile.Id);
        var source = Assert.Single(result.Document.Value!.Sources, s => s.Path == Path.Combine(documents, "profile.sav"));
        Assert.Equal(SaveSourceOrigin.Suggestion, source.Origin);
        Assert.False(source.Selected);
        Assert.True(source.CanConfirm);
        Assert.Contains("database", source.Evidence);
        Assert.Equal(SaveDiscoveryPhase.NeedsConfirmation, result.Phase);
        var confirmed = await service.ConfirmAsync(profile.Id, source.Id);
        Assert.Equal(SaveSourceOrigin.Confirmed, Assert.Single(confirmed.Document.Value!.Sources, s => s.Selected).Origin);
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public readonly List<HttpRequestMessage> Requests = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(respond(request));
        }
    }

    private static string BigManifest() => string.Join("\n", Enumerable.Range(0, 600).Select(i => $"Game {i}:\n  files:\n    <base>/s{i}:\n      tags:\n        - save\n  steam:\n    id: {1000 + i}"));

    [Fact]
    public async Task Downloading_keeps_a_valid_copy_offline_sends_nothing_personal_and_uses_the_etag()
    {
        var handler = new Handler(r => r.Headers.IfNoneMatch.Count > 0
            ? new HttpResponseMessage(HttpStatusCode.NotModified)
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(BigManifest(), Encoding.UTF8), Headers = { ETag = new System.Net.Http.Headers.EntityTagHeaderValue("\"abc\"") } });
        var database = new SaveLocationDatabase(Paths);
        var status = await database.DownloadAsync(handler, CancellationToken.None);
        Assert.True(status.Available);
        Assert.Equal(600, status.Games);
        Assert.NotNull(status.DownloadedAt);
        Assert.Equal(SaveLocationDatabase.ManifestUrl, handler.Requests[0].RequestUri!.ToString());
        Assert.Empty(handler.Requests[0].Headers.Authorization is null ? [] : new[] { "auth" });
        Assert.True(File.Exists(Path.Combine(root, "app", "Cache", "ludusavi-manifest.yaml")));
        Assert.Equal(600, new SaveLocationDatabase(Paths).Status().Games);                          // a new run reads the cached copy
        var again = await database.DownloadAsync(handler, CancellationToken.None);
        Assert.Equal(2, handler.Requests.Count);
        Assert.NotEmpty(handler.Requests[1].Headers.IfNoneMatch);
        Assert.Equal(600, again.Games);
    }

    [Fact]
    public async Task Bad_downloads_never_replace_the_existing_copy()
    {
        WriteManifest(Sample);
        var database = new SaveLocationDatabase(Paths);
        var before = File.ReadAllText(Path.Combine(root, "app", "Cache", "ludusavi-manifest.yaml"));
        await Assert.ThrowsAsync<IOException>(() => database.DownloadAsync(new Handler(_ => new(HttpStatusCode.InternalServerError)), CancellationToken.None));
        await Assert.ThrowsAsync<IOException>(() => database.DownloadAsync(new Handler(_ => new(HttpStatusCode.OK) { Content = new StringContent("<html>not a manifest</html>") }), CancellationToken.None));
        await Assert.ThrowsAsync<IOException>(() => database.DownloadAsync(new Handler(_ => new(HttpStatusCode.OK) { Content = new StringContent(Sample) }), CancellationToken.None));   // too few games
        Assert.Equal(before, File.ReadAllText(Path.Combine(root, "app", "Cache", "ludusavi-manifest.yaml")));
        Assert.Empty(Directory.GetFiles(Path.Combine(root, "app", "Cache"), "*.tmp"));
        Assert.Equal(3, database.Status().Games);
    }

    [Fact]
    public void The_attribution_names_the_sources_and_licences()
    {
        Assert.Contains("Ludusavi", SaveLocationDatabase.Attribution);
        Assert.Contains("PCGamingWiki", SaveLocationDatabase.Attribution);
        Assert.Contains("NonCommercial", SaveLocationDatabase.Attribution);
    }
}
