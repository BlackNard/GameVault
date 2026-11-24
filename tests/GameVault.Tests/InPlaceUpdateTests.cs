using System.Net;
using System.Security.Cryptography;
using System.Text;
using GameVault.Core;
using GameVault.Infrastructure.Metadata;
using GameVault.Infrastructure.Updates;

namespace GameVault.Tests;

public sealed class InPlaceUpdateTests : IDisposable
{
    private const string Repo = "BlackNard/GameVault";
    private const string Tag = "v0.1.0-preview.3";
    private const string Name = "GameVault-Setup-0.1.0-win-x64.exe";
    private readonly string root = Path.Combine(Path.GetTempPath(), "GameVault in-place update", Guid.NewGuid().ToString("N"));

    public InPlaceUpdateTests() => Directory.CreateDirectory(root);
    public void Dispose() { try { Directory.Delete(root, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }

    private static byte[] Installer(int size = 1_200_000, bool program = true)
    {
        var bytes = new byte[size];
        new Random(7).NextBytes(bytes);
        if (program) { bytes[0] = (byte)'M'; bytes[1] = (byte)'Z'; } else { bytes[0] = 1; bytes[1] = 2; }
        return bytes;
    }

    private static string Sha(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static string DownloadUrl(string name, string tag = Tag) => $"https://github.com/{Repo}/releases/download/{tag}/{name}";

    // ---------------- release parsing ----------------

    private static string Asset(string name, long size, string? digest, string? url = null) =>
        $"{{\"name\":\"{name}\",\"size\":{size},\"browser_download_url\":\"{url ?? DownloadUrl(name)}\"" + (digest is null ? "" : $",\"digest\":\"{digest}\"") + "}";

    private static string Release(string assets, string tag = Tag) =>
        $"[{{\"tag_name\":\"{tag}\",\"draft\":false,\"prerelease\":true,\"html_url\":\"https://github.com/{Repo}/releases/tag/{tag}\",\"assets\":[{assets}]}}]";

    private static UpdateInfo? Newest(string json)
    {
        using var http = new PoliteHttp("GitHub", new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)), minimumGap: TimeSpan.Zero);
        Assert.True(ReleaseVersion.TryParse("0.1.0-preview.2", out var current));
        return new UpdateChecker(http).Newest(Encoding.UTF8.GetBytes(json), current);
    }

    [Fact]
    public void A_release_with_the_installer_and_a_digest_offers_it_with_the_checksum_file_when_there_is_one()
    {
        var digest = "sha256:" + new string('a', 64);
        var both = Newest(Release(string.Join(",", Asset(Name, 93_000_000, digest), Asset(Name + ".sha256", 101, "sha256:" + new string('b', 64)), Asset("INSTALL.md", 1500, null))))!.Installer!;
        Assert.Equal(Name, both.Name);
        Assert.Equal(93_000_000, both.Size);
        Assert.Equal(new string('a', 64), both.Sha256);
        Assert.Equal(DownloadUrl(Name + ".sha256"), both.ChecksumUrl);
        Assert.Equal(DownloadUrl(Name), both.Url);

        var digestOnly = Newest(Release(Asset(Name, 93_000_000, digest)))!.Installer!;
        Assert.Null(digestOnly.ChecksumUrl);
        var fileOnly = Newest(Release(string.Join(",", Asset(Name, 93_000_000, null), Asset(Name + ".sha256", 101, null))))!.Installer!;
        Assert.Null(fileOnly.Sha256);
        Assert.NotNull(fileOnly.ChecksumUrl);
    }

    [Theory]
    [InlineData("no installer at all")]
    [InlineData("nothing to check it against")]
    [InlineData("wrong file name")]
    [InlineData("name does not match the tag")]
    [InlineData("too small")]
    [InlineData("too large")]
    [InlineData("download link points at another release")]
    [InlineData("download link on another host")]
    [InlineData("download link with a query")]
    [InlineData("bad digest text")]
    public void An_installer_that_cannot_be_checked_or_looks_wrong_is_never_offered_and_the_release_page_remains(string problem)
    {
        var good = "sha256:" + new string('c', 64);
        var assets = problem switch
        {
            "no installer at all" => Asset("INSTALL.md", 1500, good),
            "nothing to check it against" => Asset(Name, 93_000_000, null),
            "wrong file name" => Asset("GameVault-Setup-0.1.0-linux-x64.exe", 93_000_000, good),
            "name does not match the tag" => Asset("GameVault-Setup-0.9.0-win-x64.exe", 93_000_000, good),
            "too small" => Asset(Name, 100, good),
            "too large" => Asset(Name, 900_000_000, good),
            "download link points at another release" => Asset(Name, 93_000_000, good, DownloadUrl(Name, "v9.9.9")),
            "download link on another host" => Asset(Name, 93_000_000, good, $"https://evil.example/{Repo}/releases/download/{Tag}/{Name}"),
            "download link with a query" => Asset(Name, 93_000_000, good, DownloadUrl(Name) + "?x=1"),
            _ => Asset(Name, 93_000_000, "sha256:not-a-hash")
        };
        var update = Newest(Release(assets));
        Assert.NotNull(update);                                   // the notice itself still exists
        Assert.Null(update!.Installer);
    }

    [Theory]
    [InlineData("https://github.com/BlackNard/GameVault/releases/download/v0.1.0-preview.3/GameVault-Setup-0.1.0-win-x64.exe", true)]
    [InlineData("https://GitHub.com/BlackNard/GameVault/releases/download/v0.1.0-preview.3/GameVault-Setup-0.1.0-win-x64.exe", true)]
    [InlineData("http://github.com/BlackNard/GameVault/releases/download/v0.1.0-preview.3/GameVault-Setup-0.1.0-win-x64.exe", false)]
    [InlineData("https://github.com/Other/GameVault/releases/download/v0.1.0-preview.3/GameVault-Setup-0.1.0-win-x64.exe", false)]
    [InlineData("https://github.com/BlackNard/GameVault/releases/download/v0.1.0-preview.2/GameVault-Setup-0.1.0-win-x64.exe", false)]
    [InlineData("https://github.com/BlackNard/GameVault/releases/download/v0.1.0-preview.3/other.exe", false)]
    [InlineData("https://github.com/BlackNard/GameVault/releases/download/v0.1.0-preview.3/GameVault-Setup-0.1.0-win-x64.exe#x", false)]
    [InlineData("https://user@github.com/BlackNard/GameVault/releases/download/v0.1.0-preview.3/GameVault-Setup-0.1.0-win-x64.exe", false)]
    [InlineData("https://github.com:444/BlackNard/GameVault/releases/download/v0.1.0-preview.3/GameVault-Setup-0.1.0-win-x64.exe", false)]
    [InlineData("", false)]
    public void Only_the_exact_download_address_of_this_release_is_accepted(string url, bool allowed) =>
        Assert.Equal(allowed, UpdateChecker.IsDownloadLink(url, Tag, Name));

    [Fact]
    public void The_installer_details_survive_a_restart_in_the_cached_notice()
    {
        var info = new UpdateInfo("0.1.0-preview.3", Tag, $"https://github.com/{Repo}/releases/tag/{Tag}", true, null,
            new UpdateInstaller(Name, DownloadUrl(Name), 93_000_000, new string('d', 64), DownloadUrl(Name + ".sha256")));
        UpdateNoticeCache.WriteLatest(root, info);
        var back = UpdateNoticeCache.Read(root).Latest!;
        Assert.Equal(info.Installer, back.Installer);
        UpdateNoticeCache.WriteLatest(root, info with { Installer = null });
        Assert.Null(UpdateNoticeCache.Read(root).Latest!.Installer);
        File.WriteAllText(Path.Combine(root, "Cache", "update-notice.json"),
            "{\"tag\":\"v1.0.0\",\"version\":\"1.0.0\",\"url\":\"https://github.com/BlackNard/GameVault/releases/tag/v1.0.0\",\"installer\":{\"name\":5,\"url\":\"x\",\"size\":\"big\"}}");
        Assert.Null(UpdateNoticeCache.Read(root).Latest!.Installer);                          // odd installer data is ignored, the notice stays
    }

    // ---------------- download and verification ----------------

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public readonly List<string> Urls = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Urls.Add(request.RequestUri!.ToString());
            return Task.FromResult(respond(request));
        }
    }

    private static HttpResponseMessage Redirect(string to) => new(HttpStatusCode.Found) { Headers = { Location = new Uri(to) } };
    private static HttpResponseMessage Bytes(byte[] bytes) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };

    private static UpdateInfo Update(byte[] installer, string? apiDigest, bool withChecksumFile) =>
        new("0.1.0-preview.3", Tag, $"https://github.com/{Repo}/releases/tag/{Tag}", true, null,
            new UpdateInstaller(Name, DownloadUrl(Name), installer.Length, apiDigest, withChecksumFile ? DownloadUrl(Name + ".sha256") : null));

    /// <summary>A fake GitHub: the release download address redirects to the asset host like the real one.</summary>
    private static FakeHandler Github(byte[] installer, string checksumText, string assetHost = "release-assets.githubusercontent.com") => new(request =>
    {
        var url = request.RequestUri!.ToString();
        if (url.StartsWith("https://github.com/", StringComparison.Ordinal)) return Redirect($"https://{assetHost}/github-production-release-asset/1/{Path.GetFileName(request.RequestUri.AbsolutePath)}?sig=1");
        return url.Contains(".sha256", StringComparison.Ordinal) ? Bytes(Encoding.UTF8.GetBytes(checksumText)) : Bytes(installer);
    });

    [Fact]
    public async Task A_good_installer_is_downloaded_through_githubs_redirect_checked_and_kept_under_its_release_name()
    {
        var installer = Installer();
        var hash = Sha(installer);
        var handler = Github(installer, $"{hash}  {Name}\n");
        using var downloader = new UpdateDownloader(handler);
        var reports = new List<DownloadProgress>();
        var path = await downloader.DownloadAsync(Update(installer, hash, true), root, new Progress<DownloadProgress>(reports.Add), CancellationToken.None);
        Assert.Equal(Path.Combine(root, Name), path);
        Assert.Equal(installer, await File.ReadAllBytesAsync(path));
        Assert.False(File.Exists(path + ".part"));
        Assert.Contains(handler.Urls, u => u.StartsWith("https://release-assets.githubusercontent.com/", StringComparison.Ordinal));
        Assert.True(UpdateLauncher.IsStartable(path, root));
        await Task.Delay(50);
        Assert.Equal(installer.Length, reports.Max(r => r.Received));
    }

    [Fact]
    public async Task Either_checksum_alone_is_enough_and_a_checksum_file_may_name_the_file_with_a_star()
    {
        var installer = Installer();
        var hash = Sha(installer);
        using (var apiOnly = new UpdateDownloader(Github(installer, "unused")))
            Assert.True(File.Exists(await apiOnly.DownloadAsync(Update(installer, hash, false), Path.Combine(root, "a"), null, CancellationToken.None)));
        using (var fileOnly = new UpdateDownloader(Github(installer, $"{hash} *{Name}")))
            Assert.True(File.Exists(await fileOnly.DownloadAsync(Update(installer, null, true), Path.Combine(root, "b"), null, CancellationToken.None)));
        using (var upper = new UpdateDownloader(Github(installer, hash.ToUpperInvariant() + "  " + Name)))
            Assert.True(File.Exists(await upper.DownloadAsync(Update(installer, hash.ToUpperInvariant(), true), Path.Combine(root, "c"), null, CancellationToken.None)));
    }

    private static async Task<IOException> Refused(UpdateDownloader downloader, UpdateInfo update, string folder) =>
        await Assert.ThrowsAsync<IOException>(() => downloader.DownloadAsync(update, folder, null, CancellationToken.None));

    [Fact]
    public async Task A_file_that_does_not_match_the_published_checksum_is_deleted_and_never_kept()
    {
        var installer = Installer();
        var wrong = Sha(Installer(1_200_000)) == Sha(installer) ? new string('0', 64) : Sha(Encoding.UTF8.GetBytes("something else"));
        var folder = Path.Combine(root, "mismatch");
        using var downloader = new UpdateDownloader(Github(installer, $"{wrong}  {Name}"));
        var error = await Refused(downloader, Update(installer, wrong, true), folder);
        Assert.Contains("does not match", error.Message);
        Assert.Empty(Directory.GetFiles(folder));
    }

    [Fact]
    public async Task Checksums_that_disagree_or_are_missing_or_unreadable_stop_the_download_before_any_bytes_are_fetched()
    {
        var installer = Installer();
        var hash = Sha(installer);
        var other = new string('9', 64);
        var disagree = Github(installer, $"{other}  {Name}");
        using (var d1 = new UpdateDownloader(disagree)) Assert.Contains("do not agree", (await Refused(d1, Update(installer, hash, true), Path.Combine(root, "d1"))).Message);
        Assert.DoesNotContain(disagree.Urls, u => u.Contains(Name, StringComparison.Ordinal) && !u.Contains(".sha256", StringComparison.Ordinal) && u.Contains("release-assets", StringComparison.Ordinal));

        using (var d2 = new UpdateDownloader(Github(installer, "x"))) Assert.Contains("publishes no checksum", (await Refused(d2, Update(installer, null, false), Path.Combine(root, "d2"))).Message);
        using (var d3 = new UpdateDownloader(Github(installer, "not a checksum"))) Assert.Contains("could not be read", (await Refused(d3, Update(installer, null, true), Path.Combine(root, "d3"))).Message);
        using (var d4 = new UpdateDownloader(Github(installer, $"{hash}  Other-File.exe"))) Assert.Contains("could not be read", (await Refused(d4, Update(installer, null, true), Path.Combine(root, "d4"))).Message);
        using (var d5 = new UpdateDownloader(Github(installer, new string('a', 5000)))) Assert.Contains("unexpectedly large", (await Refused(d5, Update(installer, null, true), Path.Combine(root, "d5"))).Message);
    }

    [Theory]
    [InlineData("evil.example")]
    [InlineData("github.com.evil.example")]
    [InlineData("raw.githubusercontent.com")]
    [InlineData("objects.githubusercontent.com.evil.example")]
    public async Task A_redirect_to_any_other_host_is_refused(string host)
    {
        var installer = Installer();
        var handler = Github(installer, "x", host);
        using var downloader = new UpdateDownloader(handler);
        var error = await Refused(downloader, Update(installer, Sha(installer), false), Path.Combine(root, "redirect"));
        Assert.Contains("does not trust", error.Message);
        Assert.DoesNotContain(handler.Urls, u => u.Contains(host, StringComparison.Ordinal));          // nothing was requested from the untrusted host
    }

    [Fact]
    public async Task Plain_http_redirects_loops_and_missing_files_are_refused_with_plain_messages()
    {
        var installer = Installer();
        var hash = Sha(installer);
        using (var http = new UpdateDownloader(new FakeHandler(_ => Redirect("http://release-assets.githubusercontent.com/x"))))
            Assert.Contains("does not trust", (await Refused(http, Update(installer, hash, false), Path.Combine(root, "h1"))).Message);
        using (var loop = new UpdateDownloader(new FakeHandler(_ => Redirect("https://release-assets.githubusercontent.com/again"))))
            Assert.Contains("too many times", (await Refused(loop, Update(installer, hash, false), Path.Combine(root, "h2"))).Message);
        using (var gone = new UpdateDownloader(new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound))))
            Assert.Contains("no longer has", (await Refused(gone, Update(installer, hash, false), Path.Combine(root, "h3"))).Message);
        using (var broken = new UpdateDownloader(new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError))))
            Assert.Contains("status 500", (await Refused(broken, Update(installer, hash, false), Path.Combine(root, "h4"))).Message);
        using (var offline = new UpdateDownloader(new FakeHandler(_ => throw new HttpRequestException("offline"))))
            Assert.Contains("could not be reached", (await Refused(offline, Update(installer, hash, false), Path.Combine(root, "h5"))).Message);
    }

    [Fact]
    public async Task A_wrong_size_too_much_data_or_a_file_that_is_not_a_program_is_refused_and_removed()
    {
        var installer = Installer();
        var hash = Sha(installer);
        var longer = Installer(1_300_000);
        using (var tooBig = new UpdateDownloader(Github(longer, "x")))
        {
            var update = Update(installer, hash, false);                                                // the release lists a smaller size than GitHub sends
            var folder = Path.Combine(root, "size1");
            var error = await Refused(tooBig, update, folder);
            Assert.Contains("not the size", error.Message);
            Assert.Empty(Directory.Exists(folder) ? Directory.GetFiles(folder) : []);
        }
        var shortFile = new byte[installer.Length - 500];
        Array.Copy(installer, shortFile, shortFile.Length);
        var chunked = new FakeHandler(request => request.RequestUri!.Host == "github.com" ? Redirect("https://release-assets.githubusercontent.com/f") : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new ForwardOnlyStream(shortFile)) });
        using (var early = new UpdateDownloader(chunked))
        {
            var folder = Path.Combine(root, "size2");
            Assert.Contains("ended early", (await Refused(early, Update(installer, hash, false), folder)).Message);
            Assert.Empty(Directory.GetFiles(folder));
        }
        var notProgram = Installer(program: false);
        using (var text = new UpdateDownloader(Github(notProgram, "x")))
        {
            var folder = Path.Combine(root, "size3");
            Assert.Contains("not a Windows program", (await Refused(text, Update(notProgram, Sha(notProgram), false), folder)).Message);
            Assert.Empty(Directory.GetFiles(folder));
        }
    }

    [Fact]
    public async Task A_release_without_a_usable_installer_or_with_an_untrusted_link_is_not_even_requested()
    {
        var installer = Installer();
        var handler = Github(installer, "x");
        using var downloader = new UpdateDownloader(handler);
        var none = new UpdateInfo("0.1.0-preview.3", Tag, "https://github.com/x", true, null);
        Assert.Contains("no installer", (await Refused(downloader, none, root)).Message);
        var evil = Update(installer, Sha(installer), false) with { Installer = new UpdateInstaller(Name, "https://evil.example/" + Name, installer.Length, Sha(installer), null) };
        Assert.Contains("not valid", (await Refused(downloader, evil, root)).Message);
        var oddName = Update(installer, Sha(installer), false) with { Installer = new UpdateInstaller("..\\evil.exe", DownloadUrl("..\\evil.exe"), installer.Length, Sha(installer), null) };
        Assert.Contains("not valid", (await Refused(downloader, oddName, root)).Message);
        Assert.Empty(handler.Urls);
    }

    [Fact]
    public async Task Cancelling_stops_the_download_and_leaves_no_partial_file()
    {
        var installer = Installer();
        using var cancel = new CancellationTokenSource();
        var folder = Path.Combine(root, "cancel");
        var handler = new FakeHandler(request => request.RequestUri!.Host == "github.com" ? Redirect("https://release-assets.githubusercontent.com/f")
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new MemoryStream(installer)) });
        using var downloader = new UpdateDownloader(handler);
        var progress = new SyncProgress(p => { if (p.Received > 100_000) cancel.Cancel(); });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => downloader.DownloadAsync(Update(installer, Sha(installer), false), folder, progress, cancel.Token));
        Assert.Empty(Directory.GetFiles(folder));
    }

    /// <summary>A stream with no known length (like a chunked response), so only the byte count can reveal a short file.</summary>
    private sealed class ForwardOnlyStream(byte[] bytes) : Stream
    {
        private readonly MemoryStream inner = new(bytes);
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => inner.ReadAsync(buffer, cancellationToken);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class SyncProgress(Action<DownloadProgress> report) : IProgress<DownloadProgress>
    {
        public void Report(DownloadProgress value) => report(value);
    }

    [Fact]
    public async Task A_stalled_download_is_abandoned_after_the_idle_timeout()
    {
        var installer = Installer();
        var handler = new FakeHandler(request => request.RequestUri!.Host == "github.com" ? Redirect("https://release-assets.githubusercontent.com/f")
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new StallingStream(installer.Length)) });
        using var downloader = new UpdateDownloader(handler, idleTimeout: TimeSpan.FromMilliseconds(200));
        var folder = Path.Combine(root, "stall");
        Assert.Contains("stopped sending", (await Refused(downloader, Update(installer, Sha(installer), false), folder)).Message);
        Assert.Empty(Directory.GetFiles(folder));
    }

    private sealed class StallingStream(int length) : Stream
    {
        private int sent;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => length;
        public override long Position { get => sent; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (sent == 0) { sent = 1000; buffer.Span[0] = (byte)'M'; buffer.Span[1] = (byte)'Z'; return 1000 <= buffer.Length ? 1000 : buffer.Length; }
            await Task.Delay(Timeout.Infinite, cancellationToken);                                  // then nothing more arrives
            return 0;
        }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    // ---------------- cleanup, launching and install location ----------------

    [Fact]
    public void Old_downloads_are_cleaned_but_other_files_and_fresh_ones_stay_and_the_launcher_starts_only_the_release_installer_from_the_updates_folder()
    {
        var folder = Path.Combine(root, "updates");
        Directory.CreateDirectory(folder);
        var oldInstaller = Path.Combine(folder, "GameVault-Setup-0.1.0-win-x64.exe");
        var oldPart = Path.Combine(folder, "GameVault-Setup-0.2.0-win-x64.exe.part");
        var fresh = Path.Combine(folder, "GameVault-Setup-0.3.0-win-x64.exe");
        var other = Path.Combine(folder, "notes.txt");
        foreach (var file in new[] { oldInstaller, oldPart, fresh, other }) File.WriteAllText(file, "x");
        var now = DateTime.UtcNow;
        File.SetLastWriteTimeUtc(oldInstaller, now.AddDays(-5));
        File.SetLastWriteTimeUtc(oldPart, now.AddDays(-5));
        File.SetLastWriteTimeUtc(other, now.AddDays(-5));
        UpdateDownloader.CleanOld(folder, TimeSpan.FromDays(2), now);
        Assert.False(File.Exists(oldInstaller));
        Assert.False(File.Exists(oldPart));
        Assert.True(File.Exists(fresh));
        Assert.True(File.Exists(other));
        UpdateDownloader.CleanOld(Path.Combine(root, "missing"), TimeSpan.FromDays(2));          // a missing folder is fine

        Assert.True(UpdateLauncher.IsStartable(fresh, folder));
        Assert.False(UpdateLauncher.IsStartable(other, folder));
        Assert.False(UpdateLauncher.IsStartable(Path.Combine(folder, "GameVault-Setup-9.9.9-win-x64.exe"), folder));       // not on disk
        var outside = Path.Combine(root, "GameVault-Setup-0.4.0-win-x64.exe");
        File.WriteAllText(outside, "x");
        Assert.False(UpdateLauncher.IsStartable(outside, folder));                                                           // right name, wrong folder
        Assert.False(UpdateLauncher.IsStartable(Path.Combine(folder, "..", "updates", "..", "GameVault-Setup-0.4.0-win-x64.exe"), folder));
        Assert.False(UpdateLauncher.IsStartable("bad\0path", folder));
        Assert.Equal("/SILENT /NORESTART /RELAUNCH=1 /WAITFORPID=42", UpdateLauncher.BuildArguments(relaunch: true, waitForProcessId: 42));
        Assert.Equal("/SILENT /NORESTART", UpdateLauncher.BuildArguments(relaunch: false, waitForProcessId: 0));
        Assert.False(UpdateLauncher.Start(other, folder, relaunch: false, waitForProcessId: 1));                                                                   // refuses before starting anything
    }

    [Theory]
    [InlineData(@"C:\Users\Player\AppData\Local\Programs\GameVault", @"C:\Users\Player\AppData\Local\Programs\GameVault", true)]
    [InlineData(@"C:\Users\Player\AppData\Local\Programs\GameVault\", @"C:\Users\Player\AppData\Local\Programs\GameVault", true)]
    [InlineData(@"c:\users\player\appdata\local\programs\gamevault", @"C:\Users\Player\AppData\Local\Programs\GameVault\", true)]
    [InlineData(@"D:\Portable\GameVault", @"C:\Users\Player\AppData\Local\Programs\GameVault", false)]
    [InlineData(@"C:\Users\Player\AppData\Local\Programs\GameVault\sub", @"C:\Users\Player\AppData\Local\Programs\GameVault", false)]
    [InlineData(@"D:\Projects\GameVault\outputs\Builds\GameVault-x", null, false)]
    [InlineData(@"D:\Projects\GameVault", "", false)]
    [InlineData(null, @"C:\x", false)]
    [InlineData(@"C:\", @"C:\", false)]
    public void Only_the_copy_the_installer_registered_counts_as_installed(string? running, string? registered, bool expected) =>
        Assert.Equal(expected, InstallLocationRules.IsInstalledCopy(running, registered));

    [Fact]
    public void The_installer_relaunches_the_new_version_only_when_asked_and_keeps_its_data_handling_unchanged()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "GameVault.slnx"))) dir = dir.Parent;
        var iss = File.ReadAllText(Path.Combine(dir!.FullName, "installer", "GameVault.iss"));
        Assert.Contains("{param:RELAUNCH|0}", iss);                                                     // off unless /RELAUNCH=1 is passed
        Assert.Contains("Check: RelaunchRequested", iss);
        Assert.Contains("function RelaunchRequested: Boolean;", iss);
        Assert.Contains("{param:WAITFORPID|0}", iss);                                                   // setup waits for GameVault's own process to end
        Assert.Contains("function InitializeSetup: Boolean;", iss);
        Assert.DoesNotContain("UninstallDelete", iss.Replace("; Deliberately no wildcard cleanup / UninstallDelete.", ""));   // data and profiles are still never touched by setup
        Assert.Contains("PrivilegesRequired=lowest", iss);                                              // an update never asks for administrator rights
        Assert.Contains("CloseApplications=no", iss);
    }
}
