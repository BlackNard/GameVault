using System.Net;
using System.Text;
using GameVault.Core;
using GameVault.Infrastructure.Artwork;
namespace GameVault.Tests;

public sealed class SteamPortraitCoverTests
{
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        { Interlocked.Increment(ref Calls); return respond(request, token); }
    }
    private static Handler Respond(Func<HttpRequestMessage, HttpResponseMessage> respond) => new((r, _) => Task.FromResult(respond(r)));
    private static HttpResponseMessage Json(string text) => new(HttpStatusCode.OK) { Content = new StringContent(text, Encoding.UTF8, "application/json") };
    private static HttpResponseMessage Image() => new(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3]) { Headers = { ContentType = new("image/jpeg") } } };
    private static SteamPortraitCoverCatalogue Provider(Handler api, Handler? images = null) => new(new HttpClient(api), new HttpClient(images ?? Respond(_ => Image())));
    private static PortraitCoverCandidate Candidate(int id = 42) => new(id, "Game", "Steam", new Uri($"https://store.steampowered.com/app/{id}/"));

    [Theory]
    [InlineData(600,900,true)] [InlineData(1200,1800,true)] [InlineData(612,900,true)]
    [InlineData(613,900,false)] [InlineData(599,900,false)] [InlineData(600,899,false)]
    [InlineData(900,600,false)] [InlineData(0,0,false)] [InlineData(4000,6000,false)] [InlineData(6000,9000,false)]
    public void PortraitPolicyChecksResolutionRatioAndDecodeLimits(int width,int height,bool expected) => Assert.Equal(expected,PortraitCoverPolicy.IsSuitable(width,height));

    [Theory] [InlineData(@"C:\private\game")] [InlineData("/home/game")] [InlineData("folder/game")] [InlineData("file:///secret")] [InlineData("C:private")] [InlineData("Game\nSecret")] [InlineData(" ")] [InlineData("x")]
    public async Task RejectsPathsAndControlsBeforeNetwork(string query)
    {
        var handler=Respond(_=>throw new Exception()); using var provider=Provider(handler);
        var error=await Assert.ThrowsAsync<ArtworkException>(()=>provider.SearchAsync(query,default));
        Assert.Equal(ArtworkFailure.Query,error.Failure); Assert.Equal(0,handler.Calls);
    }

    [Fact] public async Task EncodesTitleAndFiltersMetadataDuplicatesAndInvalidIds()
    {
        var handler=Respond(r=>
        {
            Assert.Null(r.Headers.Authorization); Assert.False(r.Headers.Contains("Cookie"));
            Assert.Equal("https",r.RequestUri!.Scheme); Assert.Equal("store.steampowered.com",r.RequestUri.Host);
            if(r.RequestUri.AbsolutePath=="/api/storesearch/")
            {
                Assert.Contains("term=Half-Life%20%26%20Friends",r.RequestUri.AbsoluteUri);
                return Json("""{"items":[{"id":42,"name":"Game"},{"id":42,"name":"Duplicate"},{"id":0,"name":"Bad"},{"id":43,"name":"DLC"},{"id":44,"name":""}]}""");
            }
            Assert.Equal("/api/appdetails",r.RequestUri.AbsolutePath);
            return r.RequestUri.Query.Contains("42") ? Json("""{"42":{"success":true,"data":{"type":"game","name":"Game: Edition","release_date":{"date":"10 Oct, 2020"}}}}""") : Json("""{"43":{"success":true,"data":{"type":"dlc","name":"DLC"}}}""");
        });
        using var provider=Provider(handler); var result=Assert.Single(await provider.SearchAsync("Half-Life & Friends",default));
        Assert.Equal(42,result.AppId); Assert.Equal("Game: Edition",result.Title); Assert.Contains("2020",result.Context);
        Assert.Equal("https://store.steampowered.com/app/42/",result.SourcePage.AbsoluteUri); Assert.Equal(3,handler.Calls);
    }

    [Fact] public async Task MetadataConcurrencyAndCandidateCountAreBounded()
    {
        int active=0,peak=0;
        var handler=new Handler(async(r,token)=>
        {
            if(r.RequestUri!.AbsolutePath.Contains("storesearch")) return Json("{\"items\":["+string.Join(",",Enumerable.Range(1,30).Select(id=>$"{{\"id\":{id},\"name\":\"Game\"}}"))+"]}");
            var current=Interlocked.Increment(ref active); int seen; do { seen=peak; } while(current>seen&&Interlocked.CompareExchange(ref peak,current,seen)!=seen);
            try { await Task.Delay(10,token); var id=System.Web.HttpUtility.ParseQueryString(r.RequestUri.Query)["appids"]; return Json($"{{\"{id}\":{{\"success\":true,\"data\":{{\"type\":\"game\",\"name\":\"Game\"}}}}}}"); }
            finally { Interlocked.Decrement(ref active); }
        });
        using var provider=Provider(handler); Assert.Equal(10,(await provider.SearchAsync("Game",default)).Count); Assert.InRange(peak,1,2); Assert.InRange(handler.Calls,11,21);
    }

    [Fact] public async Task FixedImagePathsAndOnly404Fallback()
    {
        var paths=new List<string>(); var images=Respond(r=> { Assert.Null(r.Headers.Authorization); Assert.False(r.Headers.Contains("Cookie")); paths.Add(r.RequestUri!.AbsoluteUri); return paths.Count==2?new(HttpStatusCode.NotFound):Image(); });
        using var provider=Provider(Respond(_=>throw new Exception()),images);
        await provider.DownloadAsync(Candidate(),true,default); await provider.DownloadAsync(Candidate(),false,default);
        Assert.Equal(new[]{"https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/42/library_600x900.jpg","https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/42/library_600x900_2x.jpg","https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/42/library_600x900.jpg"},paths);
    }
    [Theory] [InlineData(302,ArtworkFailure.InvalidResponse)] [InlineData(429,ArtworkFailure.RateLimit)] [InlineData(500,ArtworkFailure.Offline)]
    public async Task FailuresAreSanitizedAndNeverFallback(int status,ArtworkFailure failure)
    {
        var handler=Respond(_=>new((HttpStatusCode)status){Content=new StringContent("private-server-secret")}); using var provider=Provider(handler,handler);
        var error=await Assert.ThrowsAsync<ArtworkException>(()=>provider.DownloadAsync(Candidate(),false,default)); Assert.Equal(failure,error.Failure); Assert.DoesNotContain("private-server-secret",error.ToString()); Assert.Equal(1,handler.Calls);
    }
    [Fact] public async Task CandidateCannotSupplyAnArbitraryDownloadUrl()
    {
        var handler=Respond(_=>Image()); using var provider=Provider(handler,handler);
        await Assert.ThrowsAsync<ArtworkException>(()=>provider.DownloadAsync(new(42,"Game","",new Uri("https://evil.example/private")),false,default));
        await Assert.ThrowsAsync<ArtworkException>(()=>provider.DownloadAsync(Candidate(-1),false,default)); Assert.Equal(0,handler.Calls);
    }
    [Fact] public async Task JsonSizeDepthAndImageTypeAreBounded()
    {
        foreach(var content in new[]{new string(' ',1_048_577),new string('[',25)+"0"+new string(']',25),"<html>secret</html>"})
        { using var provider=Provider(Respond(_=>Json(content))); await Assert.ThrowsAsync<ArtworkException>(()=>provider.SearchAsync("Game",default)); }
        using var imageProvider=Provider(Respond(_=>Json("{}")),Respond(_=>Json("{}")));
        Assert.Equal(ArtworkFailure.InvalidResponse,(await Assert.ThrowsAsync<ArtworkException>(()=>imageProvider.DownloadAsync(Candidate(),true,default))).Failure);
    }
    [Fact] public async Task CancellationBeforeAndDuringRequestIsPreserved()
    {
        var handler=new Handler(async(_,token)=> { await Task.Delay(Timeout.Infinite,token); return Image(); }); using var provider=Provider(handler);
        using var cancelled=new CancellationTokenSource();cancelled.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>provider.SearchAsync("Game",cancelled.Token)); Assert.Equal(0,handler.Calls);
        using var ongoing=new CancellationTokenSource(50); await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>provider.SearchAsync("Game",ongoing.Token));
    }
    [Fact] public async Task TransportTimeoutAndOfflineHaveSafeFailures()
    {
        using var timeout=Provider(new Handler((_,_)=>throw new TaskCanceledException("secret")));
        Assert.Equal(ArtworkFailure.Timeout,(await Assert.ThrowsAsync<ArtworkException>(()=>timeout.SearchAsync("Game",default))).Failure);
        using var offline=Provider(new Handler((_,_)=>throw new HttpRequestException("secret")));
        Assert.Equal(ArtworkFailure.Offline,(await Assert.ThrowsAsync<ArtworkException>(()=>offline.SearchAsync("Game",default))).Failure);
    }
}

public sealed class SteamPortraitCoverBoundaryTests
{
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> reply) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token) => Task.FromResult(reply(request));
    }
    private sealed class GeneratedStream(long length) : Stream
    {
        private long remaining=length;
        public override bool CanRead=>true; public override bool CanSeek=>false; public override bool CanWrite=>false;
        public override long Length=>throw new NotSupportedException(); public override long Position { get=>throw new NotSupportedException(); set=>throw new NotSupportedException(); }
        public override int Read(byte[] buffer,int offset,int count) { int read=(int)Math.Min(count,remaining);Array.Clear(buffer,offset,read);remaining-=read;return read; }
        public override void Flush()=>throw new NotSupportedException(); public override long Seek(long offset,SeekOrigin origin)=>throw new NotSupportedException(); public override void SetLength(long value)=>throw new NotSupportedException(); public override void Write(byte[] buffer,int offset,int count)=>throw new NotSupportedException();
    }
    [Fact] public async Task NonNumericIdsAreIgnoredWithoutLeakingParserFailures()
    {
        using var provider=new SteamPortraitCoverCatalogue(new HttpClient(new Handler(_=>new(HttpStatusCode.OK){Content=new StringContent("""{"items":[{"id":"secret","name":"Game"},{"id":{},"name":"Game"}]}""",Encoding.UTF8,"application/json")})),new HttpClient(new Handler(_=>throw new Exception())));
        Assert.Empty(await provider.SearchAsync("Game",default));
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task UnknownLengthStreamsCannotExceedResponseLimit(bool image)
    {
        var handler=new Handler(_=>new(HttpStatusCode.OK){Content=new StreamContent(new GeneratedStream((image?12*1024*1024:1_048_576)+1)){Headers={ContentType=new(image?"image/jpeg":"application/json")}}});
        using var provider=new SteamPortraitCoverCatalogue(new HttpClient(handler),new HttpClient(handler));
        var error=await Assert.ThrowsAsync<ArtworkException>(()=>image?provider.DownloadAsync(new(42,"Game","",new Uri("https://store.steampowered.com/app/42/")),true,default):ReadSearch(provider));
        Assert.Equal(ArtworkFailure.TooLarge,error.Failure);
    }
    private static async Task<byte[]> ReadSearch(SteamPortraitCoverCatalogue provider) {await provider.SearchAsync("Game",default);return [];}
    [Theory] [InlineData("Authorization")] [InlineData("Cookie")]
    public void RejectsInjectedClientCredentials(string header)
    {
        using var api=new HttpClient(new Handler(_=>throw new Exception())); using var images=new HttpClient(new Handler(_=>throw new Exception()));
        api.DefaultRequestHeaders.TryAddWithoutValidation(header,"private-value");
        Assert.Throws<ArgumentException>(()=>new SteamPortraitCoverCatalogue(api,images));
    }
}


public sealed class SteamPortraitCoverRedirectTests
{
    private sealed class Handler(Func<HttpRequestMessage,HttpResponseMessage> reply) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)=>Task.FromResult(reply(request)); }
    [Fact] public async Task FollowedRedirectCannotReturnBytesFromAnotherHost()
    {
        var handler=new Handler(_=>new(HttpStatusCode.OK){RequestMessage=new(HttpMethod.Get,"https://evil.example/private"),Content=new ByteArrayContent([1]){Headers={ContentType=new("image/jpeg")}}});
        using var provider=new SteamPortraitCoverCatalogue(new HttpClient(handler),new HttpClient(handler));
        Assert.Equal(ArtworkFailure.InvalidResponse,(await Assert.ThrowsAsync<ArtworkException>(()=>provider.DownloadAsync(new(42,"Game","",new Uri("https://store.steampowered.com/app/42/")),true,default))).Failure);
    }
    [Fact] public async Task BundlesAndNonAppsNeverReachAppDetails()
    {
        int calls=0;
        var handler=new Handler(_=>{calls++;return new(HttpStatusCode.OK){Content=new StringContent("""{"items":[{"id":42,"name":"Bundle","type":"bundle"},{"id":43,"name":"Package","type":"sub"}]}""",Encoding.UTF8,"application/json")};});
        using var provider=new SteamPortraitCoverCatalogue(new HttpClient(handler),new HttpClient(handler));
        Assert.Empty(await provider.SearchAsync("Game",default));Assert.Equal(1,calls);
    }
}
