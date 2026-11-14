using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using GameVault.Core;
namespace GameVault.Infrastructure.Artwork;

/// <summary>Explicit title-only SGDB requests; no profile or filesystem dependency.</summary>
public sealed class SteamGridDbProvider : IArtworkProvider, IDisposable
{
    private readonly IArtworkCredentialStore credentials;
    private readonly HttpClient api;
    private readonly HttpClient images;
    public SteamGridDbProvider(IArtworkCredentialStore credentials) : this(credentials, CreateClient(), CreateClient()) { }
    public SteamGridDbProvider(IArtworkCredentialStore credentials, HttpClient api, HttpClient images)
    {
        if (ReferenceEquals(api, images)) throw new ArgumentException("API and image clients must be separate.");
        this.credentials = credentials; this.api = api; this.images = images;
    }
    private static HttpClient CreateClient() => new(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false }) { Timeout = Timeout.InfiniteTimeSpan };
    public async Task<IReadOnlyList<ArtworkGame>> SearchAsync(string title, CancellationToken cancellationToken)
    {
        title = title.Trim();
        if (title.Length is < 2 or > 120 || title.Contains('\\') || title.StartsWith('/') || title.Contains(":/") || title.Contains(":\\") || title.Any(char.IsControl))
            throw new ArtworkException(ArtworkFailure.Query, "Enter a game title (2–120 characters), never a file or folder path.");
        using var doc = await RequestJsonAsync("search/autocomplete/" + Uri.EscapeDataString(title), cancellationToken);
        var result = new List<ArtworkGame>();
        foreach (var item in Data(doc).EnumerateArray().Take(20))
        {
            if (!item.TryGetProperty("id", out var id) || !id.TryGetInt32(out var number) || number <= 0) continue;
            var name = Text(item, "name"); if (string.IsNullOrWhiteSpace(name)) continue;
            var context = new List<string>();
            if (item.TryGetProperty("release_date", out var date) && date.TryGetInt64(out var unix))
                try { context.Add(DateTimeOffset.FromUnixTimeSeconds(unix).Year.ToString()); } catch (ArgumentOutOfRangeException) { }
            if (item.TryGetProperty("types", out var types) && types.ValueKind == JsonValueKind.Array)
                context.AddRange(types.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Take(8).Select(x => x.GetString()![..Math.Min(x.GetString()!.Length,40)]));
            result.Add(new(number, name, string.Join(" · ", context)));
        }
        return result;
    }
    public async Task<IReadOnlyList<ArtworkCover>> GetCoversAsync(int gameId, CancellationToken cancellationToken)
    {
        if(gameId<=0) throw new ArtworkException(ArtworkFailure.Query,"Choose a game first.");
        using var doc=await RequestJsonAsync($"grids/game/{gameId}?types=static&dimensions=600x900&nsfw=false&humor=false&epilepsy=false&mimes=image/png,image/jpeg", cancellationToken);
        var result=new List<ArtworkCover>();
        foreach(var item in Data(doc).EnumerateArray().Take(40))
        {
            if(!item.TryGetProperty("id",out var id)||!id.TryGetInt32(out var number)||number<=0)continue;
            if(item.TryGetProperty("mime",out var mime)&&mime.GetString() is not ("image/png" or "image/jpeg"))continue;
            if(!Uri.TryCreate(Text(item,"url"),UriKind.Absolute,out var url)||!IsAllowedImage(url)||!Uri.TryCreate(Text(item,"thumb"),UriKind.Absolute,out var thumb)||!IsAllowedImage(thumb))continue;
            var author=item.TryGetProperty("author",out var a)?Text(a,"name"):"";
            result.Add(new(number,url,thumb,string.IsNullOrEmpty(author)?"SteamGridDB":$"SteamGridDB · {author}"));
        }
        return result;
    }
    private async Task<JsonDocument> RequestJsonAsync(string endpoint,CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string? key;
        try { key=credentials.Read(); } catch { throw new ArtworkException(ArtworkFailure.Authentication,"Windows could not read the saved API key. Enter it again or use Browse."); }
        if(string.IsNullOrWhiteSpace(key))throw new ArtworkException(ArtworkFailure.Authentication,"Optional online search needs your personal SteamGridDB API key. Use Browse without an account.");
        using var request=new HttpRequestMessage(HttpMethod.Get,"https://www.steamgriddb.com/api/v2/"+endpoint);
        try { request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",key); }
        catch(FormatException) { throw new ArtworkException(ArtworkFailure.Authentication,"The saved API key has an invalid format. Replace it or use Browse."); }
        var bytes=await SendAsync(api,request,1_048_576,false,cancellationToken);
        try { var doc=JsonDocument.Parse(bytes,new JsonDocumentOptions {MaxDepth=24}); if(!doc.RootElement.TryGetProperty("success",out var success)||success.ValueKind!=JsonValueKind.True) {doc.Dispose();throw Invalid();} return doc; }
        catch(JsonException){throw Invalid();}
    }
    private static JsonElement Data(JsonDocument doc) => doc.RootElement.TryGetProperty("data",out var data)&&data.ValueKind==JsonValueKind.Array?data:throw Invalid();
    private static string Text(JsonElement element,string property) => element.ValueKind==JsonValueKind.Object&&element.TryGetProperty(property,out var value)&&value.ValueKind==JsonValueKind.String ? (value.GetString()??"")[..Math.Min(value.GetString()!.Length,240)] : "";
    public static bool IsAllowedImage(Uri uri) => uri.IsAbsoluteUri&&uri.Scheme=="https"&&uri.Port==443&&uri.UserInfo.Length==0&&uri.Fragment.Length==0&&uri.Host is "cdn.steamgriddb.com" or "cdn2.steamgriddb.com";
    public async Task<byte[]> DownloadAsync(Uri image,CancellationToken cancellationToken)
    {
        if(!IsAllowedImage(image))throw Invalid();
        using var request=new HttpRequestMessage(HttpMethod.Get,image);
        return await SendAsync(images,request,12*1024*1024,true,cancellationToken);
    }
    private static async Task<byte[]> SendAsync(HttpClient client,HttpRequestMessage request,int limit,bool image,CancellationToken cancellationToken)
    {
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);timeout.CancelAfter(TimeSpan.FromSeconds(20));
        try
        {
            using var response=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,timeout.Token);
            if(response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)throw new ArtworkException(ArtworkFailure.Authentication,"SteamGridDB rejected the key. Replace it or use Browse.");
            if((int)response.StatusCode==429)throw new ArtworkException(ArtworkFailure.RateLimit,"SteamGridDB is rate limiting requests. Wait before retrying, or use Browse.");
            if(!response.IsSuccessStatusCode)throw new ArtworkException(ArtworkFailure.Offline,"SteamGridDB could not complete the request. Retry later or use Browse.");
            if(image&&response.Content.Headers.ContentType?.MediaType is not ("image/png" or "image/jpeg"))throw Invalid();
            if(response.Content.Headers.ContentLength>limit)throw Large();
            await using var stream=await response.Content.ReadAsStreamAsync(timeout.Token);using var buffer=new MemoryStream();var chunk=new byte[16384];
            int read;while((read=await stream.ReadAsync(chunk,timeout.Token))>0){if(buffer.Length+read>limit)throw Large();buffer.Write(chunk,0,read);}
            return buffer.ToArray();
        }
        catch(OperationCanceledException) when(!cancellationToken.IsCancellationRequested){throw new ArtworkException(ArtworkFailure.Timeout,"SteamGridDB timed out. Retry or use Browse.");}
        catch(HttpRequestException){throw new ArtworkException(ArtworkFailure.Offline,"SteamGridDB is unavailable. Check your connection, retry or use Browse.");}
    }
    private static ArtworkException Invalid()=>new(ArtworkFailure.InvalidResponse,"SteamGridDB returned unsupported artwork or data. Choose another cover or use Browse.");
    private static ArtworkException Large()=>new(ArtworkFailure.TooLarge,"SteamGridDB returned a response that exceeds the safe size limit. Choose another cover or use Browse.");
    public void Dispose(){api.Dispose();images.Dispose();}
}
