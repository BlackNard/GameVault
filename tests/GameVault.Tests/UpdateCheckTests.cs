using System.Net;
using System.Text;
using GameVault.Core;
using GameVault.Infrastructure.Metadata;
using GameVault.Infrastructure.Storage;
using GameVault.Infrastructure.Updates;

namespace GameVault.Tests;

public sealed class UpdateCheckTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "GameVault update", Guid.NewGuid().ToString("N"));

    public UpdateCheckTests() => Directory.CreateDirectory(root);
    public void Dispose() { try { Directory.Delete(root, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }

    private static ReleaseVersion V(string text) { Assert.True(ReleaseVersion.TryParse(text, out var v), text); return v; }

    [Theory]
    [InlineData("0.1.0-preview.2", "0.1.0-preview.3", true)]
    [InlineData("0.1.0-preview.9", "0.1.0-preview.10", true)]        // numbers, not text
    [InlineData("0.1.0-preview.2", "0.1.0-preview.2", false)]
    [InlineData("0.1.0-preview.3", "0.1.0-preview.2", false)]
    [InlineData("0.1.0-preview.9", "0.1.0", true)]                    // the release after its previews
    [InlineData("0.1.0", "0.1.0-preview.9", false)]
    [InlineData("0.1.0", "0.1.1", true)]
    [InlineData("0.1.9", "0.2.0", true)]
    [InlineData("0.9.0", "1.0.0", true)]
    [InlineData("0.1.0", "0.1.0.1", true)]                            // optional fourth number
    [InlineData("0.1.0-preview.2", "v0.1.0-preview.3", true)]         // the tag has a leading v
    [InlineData("0.1.0-preview.2+abc123", "0.1.0-preview.2", false)]  // build metadata is ignored
    [InlineData("0.1.0-alpha", "0.1.0-beta", true)]
    [InlineData("0.1.0-preview", "0.1.0-preview.1", true)]
    [InlineData("0.1.0-1", "0.1.0-preview", true)]                    // numbers sort before words
    public void Versions_compare_like_semantic_versions_with_a_leading_v_allowed(string current, string candidate, bool newer) =>
        Assert.Equal(newer, V(candidate) > V(current));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("latest")]
    [InlineData("1.2")]
    [InlineData("v1.2.3.4.5")]
    [InlineData("1.2.x")]
    [InlineData("-1.2.3")]
    [InlineData("1.2.3-")]
    [InlineData("1.2.3-pre release")]
    [InlineData("99999999.0.0")]
    public void Odd_version_text_is_rejected(string? text) => Assert.False(ReleaseVersion.TryParse(text, out _));

    [Fact]
    public void A_version_prints_back_in_a_stable_form()
    {
        Assert.Equal("0.1.0-preview.2", V("v0.1.0-preview.2").ToString());
        Assert.Equal("0.1.0.3", V("0.1.0.3").ToString());
        Assert.Equal("1.2.3", V("1.2.3+build.5").ToString());
    }

    [Fact]
    public void The_start_up_check_runs_when_allowed_and_not_done_in_the_last_twelve_hours()
    {
        var now = new DateTimeOffset(2026, 11, 24, 12, 0, 0, TimeSpan.Zero);
        Assert.False(UpdateCheckRules.ShouldCheckWhenReopened(false, null, now));                // the player turned it off
        Assert.True(UpdateCheckRules.ShouldCheckWhenReopened(true, null, now));
        Assert.False(UpdateCheckRules.ShouldCheckWhenReopened(true, now.AddMinutes(-9), now));
        Assert.True(UpdateCheckRules.ShouldCheckWhenReopened(true, now.AddMinutes(-10), now));
        Assert.True(UpdateCheckRules.ShouldCheckWhenReopened(true, now.AddHours(3), now));       // a clock that moved back never blocks the check for ever
        Assert.True(UpdatePreferences.ParseCheckForUpdates(null));
        Assert.True(UpdatePreferences.ParseCheckForUpdates("not json"));
        Assert.True(UpdatePreferences.ParseCheckForUpdates("{\"autoUpdate\":false}"));
        Assert.False(UpdatePreferences.ParseCheckForUpdates("{\"checkForUpdates\":false}"));
    }

    [Fact]
    public void The_time_of_the_last_check_is_kept_in_the_cache_folder_and_odd_files_mean_never()
    {
        Assert.Null(UpdateCheckStamp.Read(root));
        var when = new DateTimeOffset(2026, 11, 24, 9, 30, 0, TimeSpan.Zero);
        UpdateCheckStamp.Write(root, when);
        Assert.Equal(when, UpdateCheckStamp.Read(root));
        Assert.True(File.Exists(Path.Combine(root, "Cache", "update-check.txt")));
        File.WriteAllText(Path.Combine(root, "Cache", "update-check.txt"), "not a time");
        Assert.Null(UpdateCheckStamp.Read(root));
        var blocked = Path.Combine(root, "plain-file");
        File.WriteAllText(blocked, "x");
        UpdateCheckStamp.Write(blocked, when);                                                    // "Cache" cannot be created under a file: no exception
        Assert.Null(UpdateCheckStamp.Read(blocked));
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

    private static HttpResponseMessage Json(string body, HttpStatusCode code = HttpStatusCode.OK) => new(code) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private const string Repo = "BlackNard/GameVault";

    private static string Release(string tag, bool draft = false, bool prerelease = true, string? url = null) =>
        $"{{\"tag_name\":\"{tag}\",\"draft\":{draft.ToString().ToLowerInvariant()},\"prerelease\":{prerelease.ToString().ToLowerInvariant()},\"html_url\":\"{url ?? $"https://github.com/{Repo}/releases/tag/{tag}"}\",\"published_at\":\"2026-11-24T10:00:00Z\"}}";

    private static async Task<(UpdateCheckResult Result, Handler Handler)> Check(string current, string body, HttpStatusCode code = HttpStatusCode.OK)
    {
        var handler = new Handler(_ => Json(body, code));
        using var http = new PoliteHttp("GitHub", handler, minimumGap: TimeSpan.Zero);
        return (await new UpdateChecker(http).CheckAsync(current, CancellationToken.None), handler);
    }

    [Fact]
    public async Task A_newer_published_release_is_reported_with_its_link_and_the_same_or_older_ones_are_not()
    {
        var (newer, handler) = await Check("0.1.0-preview.2", $"[{Release("v0.1.0-preview.3")},{Release("v0.1.0-preview.2")},{Release("v0.1.0-preview.1")}]");
        Assert.Equal(UpdateStatus.Available, newer.Status);
        Assert.Equal("0.1.0-preview.3", newer.Update!.Version);
        Assert.Equal($"https://github.com/{Repo}/releases/tag/v0.1.0-preview.3", newer.Update.Url);
        Assert.True(newer.Update.IsPreRelease);
        Assert.NotNull(newer.Update.PublishedAt);
        Assert.Contains("Version 0.1.0-preview.3 is available", newer.Describe());
        Assert.Single(handler.Requests);

        var (same, _) = await Check("0.1.0-preview.3", $"[{Release("v0.1.0-preview.3")},{Release("v0.1.0-preview.2")}]");
        Assert.Equal(UpdateStatus.UpToDate, same.Status);
        Assert.Contains("latest version", same.Describe());
        Assert.Equal(UpdateStatus.UpToDate, (await Check("0.1.0-preview.3", "[]")).Result.Status);
    }

    [Fact]
    public async Task The_highest_version_wins_whatever_the_order_and_drafts_and_bad_entries_are_ignored()
    {
        var body = "[" + string.Join(",",
            Release("v0.1.0-preview.4", draft: true),                                                                    // not published
            Release("v0.1.0-preview.3"),
            Release("v0.1.0", prerelease: false),                                                                        // the stable release beats every preview
            Release("v0.2.0-preview.1", url: "https://evil.example/BlackNard/GameVault/releases/tag/v0.2.0-preview.1"),  // wrong host
            Release("nonsense"),
            "{\"tag_name\":\"v9.0.0\"}",                                                                                 // no link
            "\"just text\"",
            "17") + "]";
        var (result, _) = await Check("0.1.0-preview.2", body);
        Assert.Equal("0.1.0", result.Update!.Version);
        Assert.False(result.Update.IsPreRelease);
    }

    [Theory]
    [InlineData("https://github.com/BlackNard/GameVault/releases/tag/v1.0.0", true)]
    [InlineData("https://github.com/BlackNard/GameVault/releases", true)]
    [InlineData("https://GitHub.com/BlackNard/GameVault/releases/latest", true)]
    [InlineData("http://github.com/BlackNard/GameVault/releases/tag/v1", false)]
    [InlineData("https://github.com/Someone/GameVault/releases/tag/v1", false)]
    [InlineData("https://github.com/BlackNard/GameVault-archive/releases/tag/v1", false)]
    [InlineData("https://github.com/BlackNard/GameVault/releasesX", false)]
    [InlineData("https://github.com.evil.example/BlackNard/GameVault/releases", false)]
    [InlineData("https://user:pw@github.com/BlackNard/GameVault/releases", false)]
    [InlineData("https://github.com:8443/BlackNard/GameVault/releases", false)]
    [InlineData("https://github.com/BlackNard/GameVault/issues/1", false)]
    [InlineData("file:///C:/Windows/System32/calc.exe", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("not a url", false)]
    [InlineData("", false)]
    public void Only_this_repositorys_release_links_on_github_may_ever_be_opened(string url, bool allowed)
    {
        using var http = new PoliteHttp("GitHub", new Handler(_ => Json("[]")), minimumGap: TimeSpan.Zero);
        Assert.Equal(allowed, new UpdateChecker(http).IsOurReleaseLink(url));
    }

    [Fact]
    public async Task The_request_is_one_anonymous_get_to_the_releases_list_and_sends_nothing_about_the_player()
    {
        var (_, handler) = await Check("0.1.0-preview.2", "[]");
        var request = handler.Requests.Single();
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal($"https://api.github.com/repos/{Repo}/releases?per_page=20", request.RequestUri!.ToString());
        Assert.Null(request.Headers.Authorization);
        Assert.False(request.Headers.Contains("Cookie"));
        Assert.Null(request.Content);
        var userAgent = request.Headers.UserAgent.ToString();
        Assert.StartsWith("GameVault/", userAgent);
        Assert.DoesNotContain(Environment.UserName, userAgent, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Environment.MachineName, userAgent, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("@", request.RequestUri.Query + userAgent);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.MovedPermanently)]
    public async Task A_service_problem_is_a_quiet_failed_result_and_never_an_update(HttpStatusCode code)
    {
        var (result, _) = await Check("0.1.0-preview.2", $"[{Release("v9.9.9")}]", code);
        Assert.Equal(UpdateStatus.Failed, result.Status);
        Assert.Null(result.Update);
        Assert.StartsWith("Could not check for updates", result.Describe());
    }

    [Fact]
    public async Task Garbage_oversized_or_unreadable_answers_and_an_unknown_own_version_fail_without_throwing()
    {
        Assert.Equal(UpdateStatus.Failed, (await Check("0.1.0-preview.2", "<html>not json</html>")).Result.Status);
        Assert.Equal(UpdateStatus.Failed, (await Check("0.1.0-preview.2", "{\"message\":\"object not list\"}")).Result.Status);
        Assert.Equal(UpdateStatus.Failed, (await Check("0.1.0-preview.2", new string(' ', UpdateChecker.MaxBytes + 10))).Result.Status);
        Assert.Equal(UpdateStatus.Failed, (await Check("who knows", "[]")).Result.Status);
        using var http = new PoliteHttp("GitHub", new Handler(_ => throw new HttpRequestException("offline")), minimumGap: TimeSpan.Zero);
        var offline = await new UpdateChecker(http).CheckAsync("0.1.0-preview.2", CancellationToken.None);
        Assert.Equal(UpdateStatus.Failed, offline.Status);
        Assert.Contains("could not be reached", offline.Message);
    }

    [Fact]
    public async Task The_setting_defaults_to_on_for_older_files_and_turning_it_off_is_remembered()
    {
        var paths = ApplicationPaths.FromRoot(root);
        Directory.CreateDirectory(paths.Data);
        File.WriteAllText(Path.Combine(paths.Data, "settings.json"),
            "{\"schemaVersion\":1,\"theme\":\"Midnight\",\"defaultBackupLimit\":20,\"keepAllBackups\":false,\"reduceMotion\":false}");
        var first = await new AppDataStore(paths).InitializeAsync();
        Assert.Equal(DocumentState.Ready, first.Settings.State);
        Assert.True(first.Settings.Value!.CheckForUpdates);
        await new AppDataStore(paths).SaveSettingsAsync(first.Settings.Value with { CheckForUpdates = false });
        Assert.False((await new AppDataStore(paths).InitializeAsync()).Settings.Value!.CheckForUpdates);
        Assert.True(AppSettings.Default.CheckForUpdates);
    }

    [Fact]
    public void The_version_in_the_build_properties_is_plain_and_newer_than_every_preview_that_was_published()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "GameVault.slnx"))) dir = dir.Parent;
        var props = File.ReadAllText(Path.Combine(dir!.FullName, "Directory.Build.props"));
        var version = System.Text.RegularExpressions.Regex.Match(props, @"<GameVaultVersion>([^<]+)</GameVaultVersion>").Groups[1].Value;
        Assert.Contains("<InformationalVersion>$(Version)</InformationalVersion>", props);
        Assert.True(ReleaseVersion.TryParse(version, out var built));
        Assert.False(built.IsPreRelease);
        Assert.True(built > V("0.1.0-preview.8"));
    }

    /// <summary>An installed preview 8 (the last with that numbering) is offered the first release with a plain number, and moves on from there.</summary>
    [Fact]
    public async Task A_preview_copy_is_offered_a_plain_version()
    {
        var (found, _) = await Check("0.1.0-preview.8", $"[{Release("v0.1.1")},{Release("v0.1.0-preview.8")}]");
        Assert.Equal(("0.1.1", UpdateStatus.Available), (found.Update!.Version, found.Status));
        Assert.Equal(UpdateStatus.UpToDate, (await Check("0.1.1", $"[{Release("v0.1.1")},{Release("v0.1.0-preview.8")}]")).Result.Status);
        Assert.Equal("0.2.0", (await Check("0.1.1", $"[{Release("v0.2.0")},{Release("v0.1.1")}]")).Result.Update!.Version);
    }
}
