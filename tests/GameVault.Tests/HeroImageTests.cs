using System.Net;
using System.Text;
using GameVault.Core;
using GameVault.Infrastructure.Library;
using GameVault.Infrastructure.Metadata;
using GameVault.Infrastructure.Storage;

namespace GameVault.Tests;

public sealed class HeroImageTests : IAsyncLifetime
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "gv-hero-" + Guid.NewGuid().ToString("N"));
    private ApplicationPaths paths = null!;
    private AppDataStore store = null!;
    private LibraryService library = null!;
    private GameProfile profile = null!;
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, .. new byte[200]];
    private static readonly byte[] Png = [137, 80, 78, 71, 13, 10, 26, 10, .. new byte[200]];

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(Path.Combine(root, "game"));
        paths = ApplicationPaths.FromRoot(root);
        store = new(paths);
        await store.InitializeAsync();
        var exe = Path.Combine(root, "game", "game.exe");
        File.WriteAllBytes(exe, new byte[100]);
        library = new(paths, store);
        profile = (await library.SaveAsync(new(null, "Game", exe, Path.Combine(root, "game"), []))).Profile;
    }

    public Task DisposeAsync() { Directory.Delete(root, true); return Task.CompletedTask; }

    [Theory]
    [InlineData("https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/1/library_hero.jpg", true)]
    [InlineData("https://images.gog-statics.com/abc.jpg", true)]
    [InlineData("https://cdn2.unrealengine.com/x.jpg", true)]
    [InlineData("http://shared.akamai.steamstatic.com/a.jpg", false)]
    [InlineData("https://evil.example/steamstatic.com/a.jpg", false)]
    [InlineData("https://steamstatic.com.evil.example/a.jpg", false)]
    [InlineData("https://user:pw@images.gog-statics.com/a.jpg", false)]
    [InlineData("https://images.gog-statics.com:8443/a.jpg", false)]
    [InlineData("file:///C:/secret.jpg", false)]
    [InlineData("", false)]
    public void Only_https_store_and_game_asset_hosts_are_allowed(string url, bool allowed) => Assert.Equal(allowed, HeroImages.IsAllowedUrl(url));

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public readonly List<string> Urls = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) { Urls.Add(request.RequestUri!.ToString()); return Task.FromResult(respond(request)); }
    }

    private static HttpResponseMessage Bytes(byte[] data, HttpStatusCode code = HttpStatusCode.OK) => new(code) { Content = new ByteArrayContent(data) };

    [Fact]
    public async Task The_downloader_accepts_jpeg_and_png_only_from_allowed_hosts_and_treats_404_as_no_image()
    {
        const string url = "https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/1/library_hero.jpg";
        using var jpeg = new HeroImageDownloader(new Handler(_ => Bytes(Jpeg)), minimumGap: TimeSpan.Zero);
        Assert.Equal(Jpeg, await jpeg.DownloadAsync(url, CancellationToken.None));
        using var png = new HeroImageDownloader(new Handler(_ => Bytes(Png)), minimumGap: TimeSpan.Zero);
        Assert.Equal(Png, await png.DownloadAsync(url, CancellationToken.None));
        using var missing = new HeroImageDownloader(new Handler(_ => Bytes([], HttpStatusCode.NotFound)), minimumGap: TimeSpan.Zero);
        Assert.Null(await missing.DownloadAsync(url, CancellationToken.None));
        using var html = new HeroImageDownloader(new Handler(_ => Bytes(Encoding.UTF8.GetBytes("<html>login</html>"))), minimumGap: TimeSpan.Zero);
        await Assert.ThrowsAsync<IOException>(() => html.DownloadAsync(url, CancellationToken.None));
        using var big = new HeroImageDownloader(new Handler(_ => Bytes(new byte[HeroImageDownloader.MaxBytes + 10])), minimumGap: TimeSpan.Zero);
        await Assert.ThrowsAsync<IOException>(() => big.DownloadAsync(url, CancellationToken.None));
        var handler = new Handler(_ => Bytes(Jpeg));
        using var guarded = new HeroImageDownloader(handler, minimumGap: TimeSpan.Zero);
        await Assert.ThrowsAsync<ArgumentException>(() => guarded.DownloadAsync("https://evil.example/a.jpg", CancellationToken.None));
        Assert.Empty(handler.Urls);
        using var redirect = new HeroImageDownloader(new Handler(_ => new HttpResponseMessage(HttpStatusCode.Redirect) { Headers = { Location = new Uri("https://evil.example/a.jpg") } }), minimumGap: TimeSpan.Zero);
        await Assert.ThrowsAsync<IOException>(() => redirect.DownloadAsync(url, CancellationToken.None));
    }

    [Fact]
    public async Task A_background_is_stored_in_the_profile_only_when_missing_shows_in_the_library_and_can_be_cleared()
    {
        Assert.Null((await library.ListAsync()).Games.Single().HeroFile);
        Assert.True(await library.SetHeroIfMissingAsync(profile.Id, Jpeg));
        var hero = (await library.ListAsync()).Games.Single().HeroFile!;
        Assert.EndsWith("hero.jpg", hero);
        Assert.Equal(Jpeg, File.ReadAllBytes(hero));
        Assert.False(await library.SetHeroIfMissingAsync(profile.Id, Png));                                  // never replaced
        Assert.Equal(Jpeg, File.ReadAllBytes(hero));
        await library.ClearHeroAsync(profile.Id);
        Assert.Null((await library.ListAsync()).Games.Single().HeroFile);
        Assert.True(await library.SetHeroIfMissingAsync(profile.Id, Png));
        Assert.EndsWith("hero.png", (await library.ListAsync()).Games.Single().HeroFile!);
        await library.ClearHeroAsync(profile.Id);
        await Assert.ThrowsAsync<ArgumentException>(() => library.SetHeroIfMissingAsync(profile.Id, Encoding.UTF8.GetBytes("not an image")));
        await Assert.ThrowsAsync<ArgumentException>(() => library.SetHeroIfMissingAsync(profile.Id, new byte[9 * 1024 * 1024]));
        Assert.False(await library.SetHeroIfMissingAsync(Guid.NewGuid(), Jpeg));
    }

    [Fact]
    public async Task The_stores_offer_their_background_and_the_resolver_keeps_it_as_a_field()
    {
        const string search = "{\"items\":[{\"type\":\"app\",\"name\":\"Game\",\"id\":620}]}";
        const string details = "{\"620\":{\"success\":true,\"data\":{\"short_description\":\"x\"}}}";
        using var steam = new SteamStoreProvider(new Handler(r => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(r.RequestUri!.AbsolutePath.Contains("storesearch") ? search : details, Encoding.UTF8, "application/json") }), minimumGap: TimeSpan.Zero);
        var request = new MetadataRequest(profile.Id, "Game", "x.exe", null, "620", null, null, true, false);
        var data = (await steam.GetAsync(request, CancellationToken.None))!;
        Assert.Equal("https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/620/library_hero.jpg", data.HeroUrl);
        var result = await MetadataResolver.ResolveAsync(request, [steam], MetadataSettings.Default, GameMetadataDocument.Empty(profile.Id), CancellationToken.None);
        Assert.Equal(data.HeroUrl, result.Document.Get(MetadataField.Hero)!.Text);
        await store.SaveMetadataAsync(result.Document);                                                         // a hero address is valid in the stored document
        Assert.False(GameMetadataDocument.IsValid(GameMetadataDocument.Empty(profile.Id) with { Fields = new() { ["Hero"] = new("x", false, DateTimeOffset.UtcNow, Text: "https://evil.example/a.jpg") } }, profile.Id));

        const string catalog = "{\"products\":[{\"id\":\"9\",\"title\":\"Game\",\"galaxyBackgroundImage\":\"https://images.gog-statics.com/bg.jpg\"}]}";
        using var gog = new GogStoreProvider(new Handler(r => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(r.RequestUri!.Host == "catalog.gog.com" ? catalog : "{}", Encoding.UTF8, "application/json") }), minimumGap: TimeSpan.Zero);
        Assert.Equal("https://images.gog-statics.com/bg.jpg", (await gog.GetAsync(request with { SteamAppId = null }, CancellationToken.None))!.HeroUrl);

        const string epic = "{\"namespace\":\"ns1\",\"pages\":[{\"data\":{\"about\":{\"image\":{\"src\":\"https://cdn2.unrealengine.com/hero.jpg\"},\"description\":\"d\"}}}]}";
        using var epicProvider = new EpicStoreProvider(new Handler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(epic, Encoding.UTF8, "application/json") }), minimumGap: TimeSpan.Zero);
        Assert.Equal("https://cdn2.unrealengine.com/hero.jpg", (await epicProvider.GetAsync(request with { SteamAppId = null, Identity = new GameIdentity(EpicNamespace: "ns1") }, CancellationToken.None))!.HeroUrl);
    }
}
