using System.Net;
using System.Text;
using GameVault.Core;
using GameVault.Infrastructure.Artwork;
namespace GameVault.Tests;
public sealed class ArtworkTests
{
    private sealed class Credentials : IArtworkCredentialStore
    { public string? Key = "private-test-key"; public string? Read() => Key; public void Write(string key) => Key = key; public void Delete() => Key = null; }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    { public int Calls; protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) { cancellationToken.ThrowIfCancellationRequested(); Calls++; return Task.FromResult(respond(request)); } }
    private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    [Fact] public async Task SearchSendsOnlyEncodedTitleAndParsesContext()
    {
        var handler = new Handler(r => { Assert.Equal("Bearer", r.Headers.Authorization!.Scheme); Assert.Equal("/api/v2/search/autocomplete/Half-Life%202", r.RequestUri!.AbsolutePath); return Json("""{"success":true,"data":[{"id":42,"name":"Half-Life 2","types":["steam"],"release_date":1100563200}]}"""); });
        using var provider = new SteamGridDbProvider(new Credentials(), new HttpClient(handler), new HttpClient(new Handler(_ => throw new Exception())));
        var game = Assert.Single(await provider.SearchAsync("Half-Life 2", default)); Assert.Equal(42, game.Id); Assert.Contains("steam", game.Context); Assert.Contains("2004", game.Context);
    }
    [Theory] [InlineData(@"C:\Users\Player\Secret")] [InlineData(@"\\server\save")] [InlineData("/home/player/game")] [InlineData("file:///secret")] public async Task RejectsPathsBeforeNetwork(string title)
    { var h = new Handler(_ => throw new Exception()); using var p = new SteamGridDbProvider(new Credentials(), new HttpClient(h), new HttpClient(h)); await Assert.ThrowsAsync<ArtworkException>(() => p.SearchAsync(title, default)); Assert.Equal(0,h.Calls); }
    [Fact] public async Task ProviderErrorsNeverEchoCredentialsOrResponse()
    { var h = new Handler(_ => new(HttpStatusCode.Unauthorized) {Content=new StringContent("private-test-key")}); using var p = new SteamGridDbProvider(new Credentials(),new HttpClient(h),new HttpClient(h)); var e=await Assert.ThrowsAsync<ArtworkException>(()=>p.SearchAsync("Game",default)); Assert.DoesNotContain("private-test-key",e.ToString()); Assert.Equal(ArtworkFailure.Authentication,e.Failure); }
    [Theory] [InlineData("https://evil.example/cover.png")] [InlineData("http://cdn2.steamgriddb.com/cover.png")] [InlineData("https://cdn2.steamgriddb.com.evil.example/a.png")] [InlineData("https://user@cdn2.steamgriddb.com/a.png")] public async Task RejectsUntrustedImageUrls(string url)
    { var h=new Handler(_=>throw new Exception()); using var p=new SteamGridDbProvider(new Credentials(),new HttpClient(h),new HttpClient(h)); await Assert.ThrowsAsync<ArtworkException>(()=>p.DownloadAsync(new Uri(url),default)); Assert.Equal(0,h.Calls); }
    [Fact] public async Task DownloadsNeverSendBearerAndRejectRedirects()
    { var h=new Handler(r=> {Assert.Null(r.Headers.Authorization); return new(HttpStatusCode.Redirect) {Headers={Location=new Uri("https://evil.example/a.png")}};}); using var p=new SteamGridDbProvider(new Credentials(),new HttpClient(h),new HttpClient(h)); await Assert.ThrowsAsync<ArtworkException>(()=>p.DownloadAsync(new Uri("https://cdn2.steamgriddb.com/a.png"),default)); Assert.Equal(1,h.Calls); }
    [Fact] public async Task BoundsJsonAndImagePayloads()
    { var h=new Handler(_=>Json(new string(' ',1_048_577))); using var p=new SteamGridDbProvider(new Credentials(),new HttpClient(h),new HttpClient(h)); await Assert.ThrowsAsync<ArtworkException>(()=>p.SearchAsync("Game",default)); }
    [Fact] public async Task CancellationRemainsCancellation()
    { using var cts=new CancellationTokenSource();cts.Cancel(); var h=new Handler(_=>throw new Exception()); using var p=new SteamGridDbProvider(new Credentials(),new HttpClient(h),new HttpClient(h)); await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>p.SearchAsync("Game",cts.Token)); }
    [Fact] public async Task CoversFilterUnsafeFormatsAndKeepAuthorCredit()
    { var h=new Handler(_=>Json("""{"success":true,"data":[{"id":8,"url":"https://cdn2.steamgriddb.com/grid/a.png","thumb":"https://cdn2.steamgriddb.com/thumb/a.png","mime":"image/png","author":{"name":"Artist"}},{"id":9,"url":"https://evil.example/a.png","thumb":"https://evil.example/a.png","mime":"image/png"}]}"""));using var p=new SteamGridDbProvider(new Credentials(),new HttpClient(h),new HttpClient(h));var cover=Assert.Single(await p.GetCoversAsync(42,default));Assert.Contains("Artist",cover.Credit); }
    [Fact] public async Task MissingKeyDoesNotContactProvider()
    {var c=new Credentials{Key=null};var h=new Handler(_=>throw new Exception());using var p=new SteamGridDbProvider(c,new HttpClient(h),new HttpClient(h));await Assert.ThrowsAsync<ArtworkException>(()=>p.SearchAsync("Game",default));Assert.Equal(0,h.Calls);}
    [Fact] public async Task RateLimitHasSafeRetryGuidance()
    {var h=new Handler(_=>new((HttpStatusCode)429));using var p=new SteamGridDbProvider(new Credentials(),new HttpClient(h),new HttpClient(h));var error=await Assert.ThrowsAsync<ArtworkException>(()=>p.SearchAsync("Game",default));Assert.Equal(ArtworkFailure.RateLimit,error.Failure);Assert.Contains("Wait",error.Message);}
    [Fact] public async Task ImageContentTypeAndSizeAreChecked()
    {var h=new Handler(_=>new(HttpStatusCode.OK){Content=new StringContent("<svg/>",Encoding.UTF8,"image/svg+xml")});using var p=new SteamGridDbProvider(new Credentials(),new HttpClient(h),new HttpClient(h));await Assert.ThrowsAsync<ArtworkException>(()=>p.DownloadAsync(new Uri("https://cdn2.steamgriddb.com/a.png"),default));}
    [Fact] public async Task SelectedBytesPersistWithoutProviderOrCredentialDependency()
    {
        var root=Path.Combine(Path.GetTempPath(),"GameVault-artwork-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try{
            var exe=Path.Combine(root,"game.exe");await File.WriteAllTextAsync(exe,"fixture never executed");
            var paths=ApplicationPaths.FromRoot(root);var store=new GameVault.Infrastructure.Storage.AppDataStore(paths);var library=new GameVault.Infrastructure.Library.LibraryService(paths,store);
            var png=Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jRZkAAAAASUVORK5CYII=");var expected=png.ToArray();
            await library.SaveAsync(new GameDraft(null,"Game",exe,root,[]),png);Array.Clear(png);
            var reopened=new GameVault.Infrastructure.Library.LibraryService(paths,new GameVault.Infrastructure.Storage.AppDataStore(paths));var game=Assert.Single((await reopened.ListAsync()).Games);Assert.Equal(expected,await File.ReadAllBytesAsync(game.CoverFile!));
        }finally{Directory.Delete(root,true);}
    }

}
