using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using GameVault.Core;

namespace GameVault.Infrastructure.Updates;

public readonly record struct DownloadProgress(long Received, long Total);

/// <summary>
/// Downloads a release's installer so GameVault can update itself in place, and refuses anything it cannot check. Only the file named in the release is fetched,
/// only from this repository's release download address; GitHub redirects such downloads to its own asset hosts, so a redirect is followed (at most three hops)
/// only to those fixed hosts over HTTPS. The file must be exactly the size GitHub lists and its SHA-256 must match the digest GitHub computed for it and, when the release
/// publishes one, the <c>.sha256</c> file next to it (if both exist they must agree). A file that fails any check is deleted and never reaches the installer.
/// This proves the file is the one the release published; it is not a publisher signature (the installer is unsigned).
/// </summary>
public sealed class UpdateDownloader : IDisposable
{
    public static readonly string[] AllowedRedirectHosts = ["release-assets.githubusercontent.com", "objects.githubusercontent.com", "github-releases.githubusercontent.com"];
    public const int MaxRedirects = 3;
    public const int MaxChecksumBytes = 4096;
    private static readonly Regex InstallerFile = new(@"^GameVault-Setup-[0-9.]{5,30}-win-x64\.exe$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private readonly HttpClient client;
    private readonly TimeSpan idleTimeout;
    private readonly string repository;

    public UpdateDownloader(HttpMessageHandler? handler = null, TimeSpan? idleTimeout = null, string repository = UpdateChecker.DefaultRepository)
    {
        client = new HttpClient(handler ?? new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false, UseProxy = true }) { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("GameVault", "1"));
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("(desktop game library)"));
        this.idleTimeout = idleTimeout ?? TimeSpan.FromSeconds(60);
        this.repository = repository;
    }

    /// <summary>Downloads and verifies the installer into <paramref name="folder"/> and returns its full path. Throws <see cref="IOException"/> with a plain message otherwise.</summary>
    public async Task<string> DownloadAsync(UpdateInfo update, string folder, IProgress<DownloadProgress>? progress, CancellationToken cancellationToken)
    {
        var installer = update.Installer ?? throw new IOException("This release has no installer GameVault can run.");
        if (!InstallerFile.IsMatch(installer.Name) || !UpdateChecker.IsDownloadLink(installer.Url, update.Tag, installer.Name, repository) ||
            installer.ChecksumUrl is { } sums && !UpdateChecker.IsDownloadLink(sums, update.Tag, installer.Name + ".sha256", repository) ||
            installer.Size is < UpdateChecker.MinInstallerBytes or > UpdateChecker.MaxInstallerBytes)
            throw new IOException("The release's installer details are not valid, so it was not downloaded.");

        var expected = await ExpectedHashAsync(installer, cancellationToken).ConfigureAwait(false);
        Directory.CreateDirectory(folder);
        var final = Path.Combine(folder, installer.Name);
        var part = final + ".part";
        DeleteQuietly(part);
        try
        {
            string actual;
            await using (var file = new FileStream(part, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.SequentialScan))
                actual = await CopyAsync(installer, file, progress, cancellationToken).ConfigureAwait(false);
            if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase)) throw new IOException("The downloaded file does not match the checksum published for this release, so it was deleted.");
            using (var check = File.OpenRead(part))
                if (check.ReadByte() != 'M' || check.ReadByte() != 'Z') throw new IOException("The downloaded file is not a Windows program, so it was deleted.");
            DeleteQuietly(final);
            File.Move(part, final);
            return final;
        }
        catch { DeleteQuietly(part); throw; }
    }

    private async Task<string> ExpectedHashAsync(UpdateInstaller installer, CancellationToken token)
    {
        var fromGitHub = installer.Sha256 is { Length: 64 } h && h.All(Uri.IsHexDigit) ? h.ToLowerInvariant() : null;
        string? fromFile = null;
        if (installer.ChecksumUrl is { } url)
        {
            var text = System.Text.Encoding.UTF8.GetString(await GetSmallAsync(url, token).ConfigureAwait(false));
            var parts = text.Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 1 && parts[0].Length == 64 && parts[0].All(Uri.IsHexDigit) && (parts.Length == 1 || parts[1].TrimStart('*') == installer.Name))
                fromFile = parts[0].ToLowerInvariant();
            else throw new IOException("The checksum file published for this release could not be read.");
        }
        if (fromGitHub is not null && fromFile is not null && fromGitHub != fromFile) throw new IOException("The checksums published for this release do not agree, so it was not downloaded.");
        return fromGitHub ?? fromFile ?? throw new IOException("This release publishes no checksum, so its installer was not downloaded.");
    }

    private async Task<byte[]> GetSmallAsync(string url, CancellationToken token)
    {
        using var response = await SendAsync(url, token).ConfigureAwait(false);
        await using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[1024];
        int read;
        while ((read = await stream.ReadAsync(chunk, token).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > MaxChecksumBytes) throw new IOException("The checksum file is unexpectedly large, so it was not used.");
            buffer.Write(chunk, 0, read);
        }
        return buffer.ToArray();
    }

    private async Task<string> CopyAsync(UpdateInstaller installer, Stream destination, IProgress<DownloadProgress>? progress, CancellationToken token)
    {
        using var response = await SendAsync(installer.Url, token).ConfigureAwait(false);
        if (response.Content.Headers.ContentLength is { } length && length != installer.Size) throw new IOException("The installer on GitHub is not the size the release lists, so it was not downloaded.");
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await using var source = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        var buffer = new byte[81920];
        long received = 0;
        progress?.Report(new DownloadProgress(0, installer.Size));
        while (true)
        {
            using var idle = CancellationTokenSource.CreateLinkedTokenSource(token);
            idle.CancelAfter(idleTimeout);
            int read;
            try { read = await source.ReadAsync(buffer, idle.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (!token.IsCancellationRequested) { throw new IOException("GitHub stopped sending the installer, so the download was abandoned."); }
            if (read == 0) break;
            received += read;
            if (received > installer.Size) throw new IOException("GitHub sent more data than the release lists, so the download was stopped.");
            hash.AppendData(buffer, 0, read);
            await destination.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
            progress?.Report(new DownloadProgress(received, installer.Size));
        }
        if (received != installer.Size) throw new IOException("The download ended early, so it was deleted.");
        await destination.FlushAsync(token).ConfigureAwait(false);
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    /// <summary>GET with redirects followed by hand: every hop must be HTTPS to this repository's address (first hop) or one of <see cref="AllowedRedirectHosts"/>.</summary>
    private async Task<HttpResponseMessage> SendAsync(string url, CancellationToken token)
    {
        var current = new Uri(url);
        for (var hop = 0; hop <= MaxRedirects; hop++)
        {
            HttpResponseMessage response;
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, current);
                response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            }
            catch (HttpRequestException error) { throw new IOException("GitHub could not be reached. " + error.Message, error); }
            if (response.StatusCode is HttpStatusCode.Moved or HttpStatusCode.Redirect or HttpStatusCode.RedirectMethod or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect)
            {
                var target = response.Headers.Location;
                response.Dispose();
                if (hop == MaxRedirects || target is null) throw new IOException("GitHub redirected the download too many times, so it was stopped.");
                var next = target.IsAbsoluteUri ? target : new Uri(current, target);
                if (next.Scheme != Uri.UriSchemeHttps || !next.IsDefaultPort || !string.IsNullOrEmpty(next.UserInfo) || !AllowedRedirectHosts.Contains(next.Host, StringComparer.OrdinalIgnoreCase))
                    throw new IOException("The download was redirected to an address GameVault does not trust, so it was stopped.");
                current = next;
                continue;
            }
            if (response.StatusCode == HttpStatusCode.NotFound) { response.Dispose(); throw new IOException("GitHub no longer has this file."); }
            if (!response.IsSuccessStatusCode) { var code = (int)response.StatusCode; response.Dispose(); throw new IOException($"GitHub answered with status {code}."); }
            return response;
        }
        throw new IOException("GitHub redirected the download too many times, so it was stopped.");
    }

    /// <summary>Removes old downloaded installers (and partial files) from the updates folder. Never throws.</summary>
    public static void CleanOld(string folder, TimeSpan olderThan, DateTime? now = null)
    {
        try
        {
            if (!Directory.Exists(folder)) return;
            var limit = (now ?? DateTime.UtcNow) - olderThan;
            foreach (var file in Directory.EnumerateFiles(folder))
            {
                var name = Path.GetFileName(file);
                if ((InstallerFile.IsMatch(name) || name.EndsWith(".exe.part", StringComparison.Ordinal) && InstallerFile.IsMatch(name[..^5])) && File.GetLastWriteTimeUtc(file) < limit) DeleteQuietly(file);
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }

    private static void DeleteQuietly(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }

    public void Dispose() => client.Dispose();
}

/// <summary>Starts a downloaded installer over the installed copy. Only a file in GameVault's own updates folder with the release installer's name is ever started.</summary>
public static class UpdateLauncher
{
    public const string Arguments = "/SILENT /NORESTART /RELAUNCH=1";
    private static readonly Regex InstallerFile = new(@"^GameVault-Setup-[0-9.]{5,30}-win-x64\.exe$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static bool IsStartable(string installerPath, string updatesFolder)
    {
        try
        {
            var full = Path.GetFullPath(installerPath);
            var folder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(updatesFolder));
            return string.Equals(Path.GetDirectoryName(full), folder, StringComparison.OrdinalIgnoreCase) && InstallerFile.IsMatch(Path.GetFileName(full)) && File.Exists(full);
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException) { return false; }
    }

    /// <summary>Starts the installer detached from this process (it keeps running after GameVault exits). Returns false when the file is not startable or Windows refuses.</summary>
    public static bool Start(string installerPath, string updatesFolder)
    {
        if (!IsStartable(installerPath, updatesFolder)) return false;
        try
        {
            using var process = Process.Start(new ProcessStartInfo(Path.GetFullPath(installerPath), Arguments) { UseShellExecute = false, WorkingDirectory = Path.GetFullPath(updatesFolder) });
            return process is not null;
        }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException or IOException) { return false; }
    }
}
