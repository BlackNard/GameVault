using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using GameVault.Core;

namespace GameVault.Infrastructure.Metadata;

/// <summary>
/// Description, release date, developers, publishers and genres for Steam games from the public Steam store API (no key, no account).
/// A Steam install is looked up by its numeric app id; any other game by an exact-title search of the Steam store. Requests have a timeout, a 1 MB response limit and a minimum gap between them.
/// </summary>
public sealed class SteamStoreProvider : IMatchSearchProvider, IDisposable
{
    public const string ProviderId = "steam";
    private const int MaxResponseBytes = 1024 * 1024;
    private static readonly Regex Tags = new("<[^>]+>", RegexOptions.Compiled);
    private readonly HttpClient client;
    private readonly bool ownsClient;
    private readonly TimeSpan minimumGap;
    private readonly SemaphoreSlim gate = new(1, 1);
    private DateTime lastRequest = DateTime.MinValue;

    public SteamStoreProvider(HttpMessageHandler? handler = null, TimeSpan? timeout = null, TimeSpan? minimumGap = null)
    {
        ownsClient = true;
        client = new HttpClient(handler ?? new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false, UseProxy = true }) { Timeout = timeout ?? TimeSpan.FromSeconds(10) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("GameVault", "1"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        this.minimumGap = minimumGap ?? TimeSpan.FromMilliseconds(800);
    }

    public string Id => ProviderId;
    public string DisplayName => "Steam store";
    public IReadOnlySet<MetadataField> AvailableFields { get; } = new HashSet<MetadataField>
        { MetadataField.Description, MetadataField.ReleaseDate, MetadataField.Developers, MetadataField.Publishers, MetadataField.Genres, MetadataField.Hero,
          MetadataField.About, MetadataField.Tags, MetadataField.Features, MetadataField.Platforms, MetadataField.Languages, MetadataField.AgeRating, MetadataField.CriticScore,
          MetadataField.CommunitySummary, MetadataField.Requirements, MetadataField.Links, MetadataField.AchievementCount, MetadataField.DlcCount, MetadataField.Screenshots };

    public async Task<ProviderData?> GetAsync(MetadataRequest request, CancellationToken cancellationToken)
    {
        var appId = request.SteamAppId;
        if (!ValidId(appId))
        {
            // Not known to be a Steam install: look the game up by its exact title, like a name-based provider in Playnite (the title is sent to the Steam store).
            appId = await FindAppIdAsync(request.Name, cancellationToken, BuildYear(request.ExecutablePath)).ConfigureAwait(false);
            if (appId is null) return null;
        }
        var json = await GetBytesAsync($"https://store.steampowered.com/api/appdetails?appids={appId}&l=english", cancellationToken).ConfigureAwait(false);
        var data = Parse(json, appId!);
        if (data is null) return null;
        // The store's hover card carries the community tags and the review summary. It is optional: a failure here never loses the rest.
        string[]? tags = null;
        string? summary = null;
        try
        {
            var hover = System.Text.Encoding.UTF8.GetString(await GetBytesAsync($"https://store.steampowered.com/apphoverpublic/{appId}?l=english", cancellationToken).ConfigureAwait(false));
            (tags, summary) = ParseHover(hover);
        }
        catch (Exception error) when (error is IOException or ArgumentException) { }
        // Steam's wide library banner for this app (a fixed address built from the id).
        return data with
        {
            SteamAppId = appId, HeroUrl = $"https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/{appId}/library_hero.jpg",
            Tags = tags, CommunitySummary = summary,
            Links = [.. (data.Links ?? []), $"Steam store page|https://store.steampowered.com/app/{appId}/"]
        };
    }

    /// <summary>Name, developers and release year of one app, or null when Steam has no such app. Only the id is sent.</summary>
    public async Task<SteamAppSummary?> SummaryAsync(string appId, CancellationToken cancellationToken)
    {
        if (!ValidId(appId)) return null;
        var json = await GetBytesAsync($"https://store.steampowered.com/api/appdetails?appids={appId}&l=english", cancellationToken).ConfigureAwait(false);
        if (Parse(json, appId) is not { } data) return null;
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 16 });
        var name = document.RootElement.GetProperty(appId).GetProperty("data").TryGetProperty("name", out var text) && text.ValueKind == JsonValueKind.String ? text.GetString() : null;
        if (string.IsNullOrWhiteSpace(name) || name.Length > 200) return null;
        return new(appId, name.Trim(), data.Developers ?? [], data.ReleaseDate?.Year, $"https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/{appId}/header.jpg");
    }

    private static bool ValidId(string? id) => id is { Length: > 0 and <= 10 } && id.All(char.IsAsciiDigit);

    /// <summary>Normalised title for matching: no trademark signs, case, spaces or punctuation.</summary>
    public static string NormalizeTitle(string title) => new(title.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    /// <summary>
    /// Searches the Steam store and accepts only an app whose title equals the game title (ignoring case, punctuation and trademark signs).
    /// When Steam also lists the same title with a year ("Resident Evil 2" and "Resident Evil 2 (1998)"), the build year of the game file
    /// (<paramref name="exeYear"/>) picks the edition: the newest one released no later than that year. Without a usable year the plain title is used.
    /// Two apps with exactly the same title are never guessed.
    /// </summary>
    public async Task<string?> FindAppIdAsync(string name, CancellationToken cancellationToken, int? exeYear = null)
    {
        var title = (name ?? "").Trim();
        if (title.Length is < 2 or > 120 || title.Any(char.IsControl) || title.Contains('\\') || title.Contains('/') || title.Contains("://")) return null;
        var wanted = NormalizeTitle(title);
        if (wanted.Length < 2) return null;
        foreach (var term in SearchTerms(title))
        {
            var (exact, siblings) = await SearchExactAsync(term, wanted, cancellationToken).ConfigureAwait(false);
            if (exact.Count > 1) return null;                         // two apps with exactly this title: never guess
            if (exact.Count == 1) return await PickEditionAsync(exact[0], siblings, exeYear, cancellationToken).ConfigureAwait(false);
        }
        return null;
    }

    /// <summary>
    /// The queries to try, best first. Steam's store search finds nothing when the query contains " - " (it works with a space or a colon), so the
    /// title is asked with the dash removed first ("Grand Theft Auto IV - The Complete Edition" finds Steam's "Grand Theft Auto IV: The Complete Edition"),
    /// then as typed, then the part before the subtitle. Whatever comes back must still equal the full title once punctuation is ignored.
    /// </summary>
    public static IReadOnlyList<string> SearchTerms(string title)
    {
        var typed = System.Text.RegularExpressions.Regex.Replace((title ?? "").Trim(), @"\s+", " ");
        var terms = new List<string>();
        void Add(string term)
        {
            term = term.Trim();
            if (term.Length >= 2 && !terms.Contains(term, StringComparer.OrdinalIgnoreCase)) terms.Add(term);
        }
        Add(System.Text.RegularExpressions.Regex.Replace(typed, @"\s+[-\u2013\u2014]\s+", " "));
        Add(typed);
        var cut = System.Text.RegularExpressions.Regex.Match(typed, @"^(.{4,}?)(?:\s+[-\u2013\u2014]\s+|:\s)");
        if (cut.Success) Add(cut.Groups[1].Value);
        return [.. terms.Take(3)];
    }

    private async Task<(List<string> Exact, List<(string Id, int Year)> Siblings)> SearchExactAsync(string term, string wanted, CancellationToken cancellationToken)
    {
        var json = await GetBytesAsync("https://store.steampowered.com/api/storesearch/?term=" + Uri.EscapeDataString(term) + "&l=english&cc=US", cancellationToken).ConfigureAwait(false);
        var exact = new List<string>();
        var siblings = new List<(string Id, int Year)>();
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 16 });
            if (document.RootElement.ValueKind != JsonValueKind.Object || !document.RootElement.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array) return (exact, siblings);
            foreach (var item in items.EnumerateArray().Take(50))
            {
                if (item.ValueKind != JsonValueKind.Object) continue;
                if (item.TryGetProperty("type", out var type) && !(type.ValueKind == JsonValueKind.String && type.GetString() == "app")) continue;
                if (!item.TryGetProperty("name", out var itemName) || itemName.ValueKind != JsonValueKind.String) continue;
                if (!item.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.Number || !id.TryGetInt64(out var number) || number <= 0 || number > 9_999_999_999) continue;
                var text = number.ToString(System.Globalization.CultureInfo.InvariantCulture);
                var normalized = NormalizeTitle(itemName.GetString() ?? "");
                if (normalized == wanted) { if (!exact.Contains(text)) exact.Add(text); }
                else if (normalized.Length == wanted.Length + 4 && normalized.StartsWith(wanted, StringComparison.Ordinal) && int.TryParse(normalized[wanted.Length..], out var year) && year is >= 1970 and <= 2100)
                    siblings.Add((text, year));
            }
        }
        catch (JsonException) { throw new IOException("Steam sent an answer GameVault could not read."); }
        return (exact, siblings);
    }

    private async Task<string> PickEditionAsync(string plainId, List<(string Id, int Year)> siblings, int? exeYear, CancellationToken cancellationToken)
    {
        if (siblings.Count == 0 || exeYear is not { } built) return plainId;
        // The plain title has no year in its name, so ask Steam when it was released.
        var plainYear = await ReleaseYearAsync(plainId, cancellationToken).ConfigureAwait(false);
        var choices = siblings.Select(x => (x.Id, Year: (int?)x.Year)).Append((Id: plainId, Year: plainYear)).Where(x => x.Year is not null).ToList();
        if (choices.Count == 0) return plainId;
        var fit = choices.Where(x => x.Year <= built + 1).OrderByDescending(x => x.Year).FirstOrDefault();
        return fit.Id ?? plainId;
    }

    private async Task<int?> ReleaseYearAsync(string appId, CancellationToken token)
    {
        try { return Parse(await GetBytesAsync($"https://store.steampowered.com/api/appdetails?appids={appId}&l=english", token).ConfigureAwait(false), appId)?.ReleaseDate?.Year; }
        catch (IOException) { return null; }
    }

    /// <summary>The year the game program was built (from its PE header), or null when the header has no believable date.</summary>
    public static int? BuildYear(string? executablePath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath)) return null;
            using var stream = new FileStream(executablePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var pe = new System.Reflection.PortableExecutable.PEReader(stream);
            var stamp = pe.PEHeaders.CoffHeader.TimeDateStamp;
            if (stamp == 0) return null;
            var year = DateTimeOffset.FromUnixTimeSeconds(stamp).Year;
            return year >= 1995 && year <= DateTime.UtcNow.Year + 1 ? year : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or BadImageFormatException or InvalidOperationException or ArgumentException) { return null; }
    }

    /// <summary>The Steam apps whose store search matches the title, for the "Not the right game?" picker.</summary>
    public async Task<IReadOnlyList<MatchCandidate>> SearchAsync(string title, CancellationToken cancellationToken)
    {
        title = (title ?? "").Trim();
        if (!PoliteHttp.SafeTitle(title)) return [];
        var result = new List<MatchCandidate>();
        foreach (var term in SearchTerms(title))
        {
            await AddCandidatesAsync(term, result, cancellationToken).ConfigureAwait(false);
            if (result.Count > 0) break;
        }
        return result;
    }

    private async Task AddCandidatesAsync(string term, List<MatchCandidate> result, CancellationToken cancellationToken)
    {
        var json = await GetBytesAsync("https://store.steampowered.com/api/storesearch/?term=" + Uri.EscapeDataString(term) + "&l=english&cc=US", cancellationToken).ConfigureAwait(false);
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 16 });
            if (document.RootElement.ValueKind != JsonValueKind.Object || !document.RootElement.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array) return;
            foreach (var item in items.EnumerateArray().Take(30))
            {
                if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String || type.GetString() != "app") continue;
                if (!item.TryGetProperty("name", out var name) || name.ValueKind != JsonValueKind.String || !item.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.Number || !id.TryGetInt64(out var number) || number <= 0 || number > 9_999_999_999) continue;
                var image = item.TryGetProperty("tiny_image", out var img) && img.ValueKind == JsonValueKind.String && img.GetString() is { } u && u.StartsWith("https://", StringComparison.Ordinal) ? u : null;
                result.Add(new("steam", number.ToString(System.Globalization.CultureInfo.InvariantCulture), (name.GetString() ?? "").Trim(), "Steam · app " + number, image));
                if (result.Count == 8) break;
            }
        }
        catch (JsonException) { throw new IOException("Steam sent an answer GameVault could not read."); }
    }

    private async Task<byte[]> GetBytesAsync(string url, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var wait = lastRequest + minimumGap - DateTime.UtcNow;
            if (wait > TimeSpan.Zero) await Task.Delay(wait, cancellationToken).ConfigureAwait(false);
            lastRequest = DateTime.UtcNow;
            using var message = new HttpRequestMessage(HttpMethod.Get, url);
            using var response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.TooManyRequests) throw new IOException("Steam asked GameVault to slow down. Try again in a few minutes.");
            if (!response.IsSuccessStatusCode) throw new IOException($"Steam answered with status {(int)response.StatusCode}.");
            if (response.Content.Headers.ContentLength > MaxResponseBytes) throw new IOException("Steam sent an unexpectedly large answer.");
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var buffer = new MemoryStream();
            var chunk = new byte[16 * 1024];
            int read;
            while ((read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
            {
                if (buffer.Length + read > MaxResponseBytes) throw new IOException("Steam sent an unexpectedly large answer.");
                buffer.Write(chunk, 0, read);
            }
            return buffer.ToArray();
        }
        catch (HttpRequestException error) { throw new IOException("Steam could not be reached. " + error.Message, error); }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested) { throw new IOException("Steam did not answer in time."); }
        finally { gate.Release(); }
    }

    public static ProviderData? Parse(byte[] json, string appId)
    {
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 16 });
            if (document.RootElement.ValueKind != JsonValueKind.Object || !document.RootElement.TryGetProperty(appId, out var entry) || entry.ValueKind != JsonValueKind.Object) return null;
            if (!entry.TryGetProperty("success", out var success) || success.ValueKind != JsonValueKind.True) return null;
            if (!entry.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object) return null;
            string? Text(string name) => data.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
            string[] List(string name) => data.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Array
                ? [.. p.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!).Where(s => !string.IsNullOrWhiteSpace(s)).Take(GameMetadataDocument.MaxItems)] : [];
            var description = Clean(Text("short_description") ?? Text("about_the_game"));
            PartialDate? release = null;
            if (data.TryGetProperty("release_date", out var date) && date.ValueKind == JsonValueKind.Object &&
                !(date.TryGetProperty("coming_soon", out var soon) && soon.ValueKind == JsonValueKind.True) &&
                date.TryGetProperty("date", out var text) && text.ValueKind == JsonValueKind.String) release = PartialDate.Parse(text.GetString());
            string[] genres = data.TryGetProperty("genres", out var g) && g.ValueKind == JsonValueKind.Array
                ? [.. g.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.Object && e.TryGetProperty("description", out var d) && d.ValueKind == JsonValueKind.String).Select(e => e.GetProperty("description").GetString()!).Take(GameMetadataDocument.MaxItems)] : [];
            var details = ExtraDetails(data);
            return new ProviderData(description, release, List("developers"), List("publishers"), genres,
                About: Clean(Text("about_the_game"), GameMetadataDocument.MaxAboutLength), Features: details.Features, Platforms: details.Platforms, Languages: details.Languages,
                AgeRating: details.AgeRating, CriticScore: details.CriticScore, CriticUrl: details.CriticUrl, Requirements: details.Requirements, Links: details.Links,
                AchievementCount: details.Achievements, DlcCount: details.Dlc, Screenshots: details.Screenshots);
        }
        catch (JsonException) { throw new IOException("Steam sent an answer GameVault could not read."); }
    }

    private sealed record Extra(string[]? Features, string[]? Platforms, string[]? Languages, string? AgeRating, int? CriticScore, string? CriticUrl, string? Requirements, string[]? Links, int? Achievements, int? Dlc, string[]? Screenshots);

    private static Extra ExtraDetails(JsonElement data)
    {
        string? Text(JsonElement item, string name) => item.ValueKind == JsonValueKind.Object && item.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
        string[]? Features = null, Platforms = null, Languages = null, Screenshots = null;
        if (data.TryGetProperty("categories", out var categories) && categories.ValueKind == JsonValueKind.Array)
            Features = [.. categories.EnumerateArray().Select(c => Text(c, "description")).OfType<string>().Take(GameMetadataDocument.MaxItems)];
        if (data.TryGetProperty("platforms", out var platforms) && platforms.ValueKind == JsonValueKind.Object)
            Platforms = [.. new[] { ("windows", "Windows"), ("mac", "macOS"), ("linux", "Linux") }.Where(p => platforms.TryGetProperty(p.Item1, out var v) && v.ValueKind == JsonValueKind.True).Select(p => p.Item2)];
        if (Text(data, "supported_languages") is { } languages)
        {
            var list = languages.Split("<br>", 2)[0];
            Languages = [.. WebUtility.HtmlDecode(Tags.Replace(list, "")).Split(',').Select(l => l.Replace("*", "").Trim()).Where(l => l.Length > 0 && l.Length <= 60).Take(GameMetadataDocument.MaxItems)];
        }
        string? age = null;
        if (data.TryGetProperty("ratings", out var ratings) && ratings.ValueKind == JsonValueKind.Object)
        {
            var parts = new List<string>();
            foreach (var (key, label) in new[] { ("pegi", "PEGI"), ("esrb", "ESRB"), ("usk", "USK"), ("cero", "CERO") })
                if (ratings.TryGetProperty(key, out var r) && Text(r, "rating") is { Length: > 0 and <= 12 } value && value.All(c => char.IsLetterOrDigit(c) || c is '+' or '-')) parts.Add($"{label} {value.ToUpperInvariant()}");
            age = parts.Count > 0 ? string.Join(" · ", parts.Take(2)) : null;
        }
        if (age is null && data.TryGetProperty("required_age", out var required))
        {
            var years = required.ValueKind == JsonValueKind.Number && required.TryGetInt32(out var n) ? n : required.ValueKind == JsonValueKind.String && int.TryParse(required.GetString(), out var m) ? m : 0;
            if (years > 0) age = $"Age {years}+";
        }
        int? score = null;
        string? scoreUrl = null;
        if (data.TryGetProperty("metacritic", out var meta) && meta.ValueKind == JsonValueKind.Object)
        {
            if (meta.TryGetProperty("score", out var sc) && sc.ValueKind == JsonValueKind.Number && sc.TryGetInt32(out var value) && value is >= 0 and <= 100) score = value;
            scoreUrl = Text(meta, "url");
        }
        string? requirements = null;
        if (data.TryGetProperty("pc_requirements", out var requirementsElement) && requirementsElement.ValueKind == JsonValueKind.Object)
        {
            var minimum = PoliteHttp.PlainText(Text(requirementsElement, "minimum"));
            var recommended = PoliteHttp.PlainText(Text(requirementsElement, "recommended"));
            requirements = string.Join("\n\n", new[] { minimum, recommended }.Where(t => t is not null));
            if (requirements.Length == 0) requirements = null;
        }
        var links = new List<string>();
        if (Text(data, "website") is { } website && website.StartsWith("https://", StringComparison.Ordinal)) links.Add("Website|" + website.Trim());
        if (data.TryGetProperty("support_info", out var support) && Text(support, "url") is { } supportUrl && supportUrl.StartsWith("https://", StringComparison.Ordinal)) links.Add("Support|" + supportUrl.Trim());
        int? achievements = data.TryGetProperty("achievements", out var a) && a.ValueKind == JsonValueKind.Object && a.TryGetProperty("total", out var total) && total.TryGetInt32(out var count) && count >= 0 ? count : null;
        int? dlc = data.TryGetProperty("dlc", out var d) && d.ValueKind == JsonValueKind.Array ? d.GetArrayLength() : null;
        if (data.TryGetProperty("screenshots", out var shots) && shots.ValueKind == JsonValueKind.Array)
            Screenshots = [.. shots.EnumerateArray().Select(x => Text(x, "path_thumbnail")).OfType<string>().Where(HeroImages.IsAllowedUrl).Take(GameMetadataDocument.MaxScreenshots)];
        return new(Features, Platforms, Languages, age, score, scoreUrl, requirements, [.. links], achievements, dlc, Screenshots);
    }

    private static readonly Regex TagDiv = new("<div class=\"app_tag\">(.*?)</div>", RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex Summary = new("<span class=\"game_review_summary[^\"]*\">(.*?)</span>\\s*\\(([\\d,]+) reviews?\\)", RegexOptions.Compiled | RegexOptions.Singleline);

    /// <summary>User tags and the review summary from the store's hover card (HTML). Missing parts give null.</summary>
    public static (string[]? Tags, string? Summary) ParseHover(string html)
    {
        if (string.IsNullOrWhiteSpace(html) || html.Length > 200_000) return (null, null);
        var tags = TagDiv.Matches(html).Select(m => Clean(m.Groups[1].Value)).OfType<string>().Where(t => t.Length <= 60).Take(GameMetadataDocument.MaxItems).ToArray();
        var summary = Summary.Match(html) is { Success: true } m && Clean(m.Groups[1].Value) is { } rating ? $"{rating} ({m.Groups[2].Value} reviews)" : null;
        return (tags.Length > 0 ? tags : null, summary);
    }

    private static string? Clean(string? html, int max = GameMetadataDocument.MaxTextLength) => PoliteHttp.PlainText(html, max);

    public void Dispose() { if (ownsClient) client.Dispose(); gate.Dispose(); }
}
