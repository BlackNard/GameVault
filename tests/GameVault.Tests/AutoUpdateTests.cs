using System.Text;
using GameVault.Core;
using GameVault.Infrastructure.Storage;
using GameVault.Infrastructure.Updates;

namespace GameVault.Tests;

public sealed class AutoUpdateTests : IDisposable
{
    private const string Name = "GameVault-Setup-0.1.0-win-x64.exe";
    private readonly string root = Path.Combine(Path.GetTempPath(), "GameVault auto update", Guid.NewGuid().ToString("N"));

    public AutoUpdateTests() => Directory.CreateDirectory(root);
    public void Dispose() { try { Directory.Delete(root, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }

    private static readonly DateTimeOffset Now = new(2026, 11, 25, 9, 0, 0, TimeSpan.Zero);
    private static ReleaseVersion V(string text) { Assert.True(ReleaseVersion.TryParse(text, out var v), text); return v; }

    private static UpdateInfo Update(string tag = "v0.1.0-preview.3", bool installer = true) =>
        new(tag.TrimStart('v'), tag, "https://github.com/BlackNard/GameVault/releases/tag/" + tag, true, null,
            installer ? new UpdateInstaller(Name, "https://github.com/BlackNard/GameVault/releases/download/" + tag + "/" + Name, 93_000_000, new string('a', 64), null) : null);

    private static PendingUpdate Pending(string tag = "v0.1.0-preview.3", string version = "0.1.0-preview.3", string sha = "", int attempts = 0) =>
        new(tag, version, Name, sha.Length == 64 ? sha : new string('b', 64), 1234, Now, attempts);

    // ---------------- rules ----------------

    [Fact]
    public void A_found_update_downloads_in_the_background_only_when_automatic_in_place_possible_and_no_game_runs()
    {
        var update = Update();
        Assert.True(AutoUpdateRules.ShouldDownload(autoUpdate: true, canUpdateInPlace: true, gameRunning: false, pending: null, update));
        Assert.False(AutoUpdateRules.ShouldDownload(false, true, false, null, update));                  // the player turned automatic updates off
        Assert.False(AutoUpdateRules.ShouldDownload(true, false, false, null, update));                  // portable or development copy
        Assert.False(AutoUpdateRules.ShouldDownload(true, true, true, null, update));                    // never compete with a running game
        Assert.False(AutoUpdateRules.ShouldDownload(true, true, false, null, Update(installer: false))); // nothing verifiable to download
        Assert.False(AutoUpdateRules.ShouldDownload(true, true, false, Pending(), update));              // already waiting
        Assert.True(AutoUpdateRules.ShouldDownload(true, true, false, Pending("v0.1.0-preview.2", "0.1.0-preview.2"), update));   // an older waiting update is replaced
        Assert.True(AutoUpdateRules.ShouldDownload(true, true, false, Pending(attempts: AutoUpdateRules.MaxAttempts), update));    // a given-up one may be fetched afresh
    }

    [Fact]
    public void A_waiting_update_installs_only_when_automatic_newer_and_not_failing_repeatedly()
    {
        var running = V("0.1.0-preview.2");
        Assert.True(AutoUpdateRules.ShouldInstall(true, Pending(), running));
        Assert.False(AutoUpdateRules.ShouldInstall(false, Pending(), running));
        Assert.False(AutoUpdateRules.ShouldInstall(true, null, running));
        Assert.False(AutoUpdateRules.ShouldInstall(true, Pending(), V("0.1.0-preview.3")));              // already the running version
        Assert.False(AutoUpdateRules.ShouldInstall(true, Pending(), V("0.2.0")));
        Assert.False(AutoUpdateRules.ShouldInstall(true, Pending(version: "garbage"), running));
        Assert.True(AutoUpdateRules.ShouldInstall(true, Pending(attempts: AutoUpdateRules.MaxAttempts - 1), running));
        Assert.False(AutoUpdateRules.ShouldInstall(true, Pending(attempts: AutoUpdateRules.MaxAttempts), running));
        Assert.True(AutoUpdateRules.HasGivenUp(Pending(attempts: AutoUpdateRules.MaxAttempts), running));
        Assert.False(AutoUpdateRules.HasGivenUp(Pending(attempts: 1), running));
        Assert.False(AutoUpdateRules.HasGivenUp(Pending(attempts: 5), V("0.1.0-preview.3")));            // it did get installed after all
        Assert.True(AutoUpdateRules.IsInstalledAlready(Pending(), V("0.1.0-preview.3")));
        Assert.False(AutoUpdateRules.IsInstalledAlready(Pending(), running));
        Assert.InRange(AutoUpdateRules.RecheckInterval, TimeSpan.FromHours(1), TimeSpan.FromHours(24));
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("{ broken", true)]
    [InlineData("[1]", true)]
    [InlineData("{\"schemaVersion\":1}", true)]
    [InlineData("{\"autoUpdate\":true,\"checkForUpdates\":true}", true)]
    [InlineData("{\"autoUpdate\":false}", false)]
    [InlineData("{\"checkForUpdates\":false}", false)]                       // no check, so nothing installs on its own
    [InlineData("{\"autoUpdate\":\"no\"}", true)]
    public void Automatic_updates_are_on_unless_the_player_turned_them_or_the_check_off(string? json, bool expected) =>
        Assert.Equal(expected, UpdatePreferences.ParseAutoUpdate(json));

    [Fact]
    public async Task The_setting_defaults_to_on_for_older_files_and_off_is_remembered()
    {
        var paths = ApplicationPaths.FromRoot(root);
        Directory.CreateDirectory(paths.Data);
        File.WriteAllText(Path.Combine(paths.Data, "settings.json"), "{\"schemaVersion\":1,\"theme\":\"Midnight\",\"defaultBackupLimit\":20,\"keepAllBackups\":false,\"reduceMotion\":false}");
        var first = await new AppDataStore(paths).InitializeAsync();
        Assert.Equal(DocumentState.Ready, first.Settings.State);
        Assert.True(first.Settings.Value!.AutoUpdate);
        await new AppDataStore(paths).SaveSettingsAsync(first.Settings.Value with { AutoUpdate = false });
        var text = await File.ReadAllTextAsync(Path.Combine(paths.Data, "settings.json"));
        Assert.False(UpdatePreferences.ParseAutoUpdate(text));                                              // the start-up code reads the very same file
        Assert.False((await new AppDataStore(paths).InitializeAsync()).Settings.Value!.AutoUpdate);
        Assert.True(AppSettings.Default.AutoUpdate);
    }

    // ---------------- the waiting update ----------------

    private string Folder => Path.Combine(root, "updates");

    private PendingUpdate WriteInstaller(PendingUpdateStore store, byte[]? content = null)
    {
        Directory.CreateDirectory(Folder);
        content ??= Encoding.ASCII.GetBytes("MZ pretend installer");
        var path = Path.Combine(Folder, Name);
        File.WriteAllBytes(path, content);
        var pending = new PendingUpdate("v0.1.0-preview.3", "0.1.0-preview.3", Name, PendingUpdateStore.HashFile(path), content.Length, Now);
        store.Write(pending);
        return pending;
    }

    [Fact]
    public void The_waiting_update_is_remembered_with_its_attempts_and_forgotten_with_its_file()
    {
        var store = new PendingUpdateStore(Folder);
        Assert.Null(store.Read());
        var pending = WriteInstaller(store);
        var back = store.Read()!;
        Assert.Equal(pending with { DownloadedAt = back.DownloadedAt }, back);
        Assert.Equal(Now, back.DownloadedAt);
        Assert.Equal(0, back.Attempts);
        Assert.True(store.IsIntact(back));

        var counted = store.RecordAttempt(back, Now.AddMinutes(5));
        Assert.Equal(1, counted.Attempts);
        Assert.Equal(Now.AddMinutes(5), store.Read()!.LastAttemptAt);
        Assert.Equal(2, store.RecordAttempt(store.Read()!, Now.AddMinutes(6)).Attempts);
        Assert.Equal(Path.Combine(Folder, Name), store.InstallerPath(back));

        store.Clear();
        Assert.Null(store.Read());
        Assert.False(File.Exists(Path.Combine(Folder, Name)));
        store.Clear();                                                                                        // nothing left: still fine
    }

    [Fact]
    public void A_file_changed_replaced_shortened_or_deleted_after_the_download_is_never_intact()
    {
        var store = new PendingUpdateStore(Folder);
        var pending = WriteInstaller(store);
        var path = Path.Combine(Folder, Name);
        Assert.True(store.IsIntact(pending));
        File.WriteAllBytes(path, Encoding.ASCII.GetBytes("MZ pretend installer"));                           // same bytes again: still the same file
        Assert.True(store.IsIntact(pending));
        File.WriteAllBytes(path, Encoding.ASCII.GetBytes("MZ pretend installeX"));                           // one byte swapped
        Assert.False(store.IsIntact(pending));
        File.WriteAllBytes(path, Encoding.ASCII.GetBytes("MZ pretend"));                                      // shortened
        Assert.False(store.IsIntact(pending));
        File.Delete(path);
        Assert.False(store.IsIntact(pending));
        Assert.False(store.IsIntact(pending with { InstallerName = "..\\evil.exe" }));                       // a name that is not the release installer is never read
    }

    [Fact]
    public void Odd_or_damaged_state_files_mean_nothing_is_waiting()
    {
        Directory.CreateDirectory(Folder);
        var file = Path.Combine(Folder, PendingUpdateStore.FileName);
        var store = new PendingUpdateStore(Folder);
        var sha = new string('c', 64);
        foreach (var text in new[]
        {
            "", "{ broken", "[1]", "{}",
            $"{{\"tag\":\"v1\",\"version\":\"1.0.0\",\"installer\":\"evil.exe\",\"sha256\":\"{sha}\",\"size\":5}}",                                  // not the installer name
            $"{{\"tag\":\"v1\",\"version\":\"1.0.0\",\"installer\":\"{Name}\",\"sha256\":\"short\",\"size\":5}}",                                   // not a hash
            $"{{\"tag\":\"v1\",\"version\":\"nonsense\",\"installer\":\"{Name}\",\"sha256\":\"{sha}\",\"size\":5}}",                               // not a version
            $"{{\"tag\":\"v1\",\"version\":\"1.0.0\",\"installer\":\"{Name}\",\"sha256\":\"{sha}\",\"size\":0}}",                                  // no size
            $"{{\"tag\":\"v1\",\"version\":\"1.0.0\",\"installer\":\"{Name}\",\"sha256\":\"{sha}\",\"size\":5,\"pad\":\"{new string('x', 5000)}\"}}" // too large
        })
        {
            File.WriteAllText(file, text);
            Assert.Null(store.Read());
        }
        File.WriteAllText(file, $"{{\"tag\":\"v1\",\"version\":\"1.0.0\",\"installer\":\"{Name}\",\"sha256\":\"{sha.ToUpperInvariant()}\",\"size\":5,\"attempts\":9999}}");
        var odd = store.Read()!;
        Assert.Equal(sha, odd.Sha256);                       // normalised to lower case
        Assert.Equal(0, odd.Attempts);                       // an absurd attempt count is ignored
        var blocked = Path.Combine(root, "plain-file");
        File.WriteAllText(blocked, "x");
        var broken = new PendingUpdateStore(Path.Combine(blocked, "sub"));
        broken.Write(Pending());                              // the folder cannot be created: no exception
        Assert.Null(broken.Read());
        Assert.False(broken.IsIntact(Pending()));
    }

    [Fact]
    public void Clean_up_keeps_the_waiting_installer_even_when_it_is_old()
    {
        var store = new PendingUpdateStore(Folder);
        WriteInstaller(store);
        var old = Path.Combine(Folder, "GameVault-Setup-0.0.9-win-x64.exe");
        File.WriteAllText(old, "x");
        var past = DateTime.UtcNow.AddDays(-10);
        File.SetLastWriteTimeUtc(old, past);
        File.SetLastWriteTimeUtc(Path.Combine(Folder, Name), past);
        UpdateDownloader.CleanOld(Folder, TimeSpan.FromDays(2), keep: Name);
        Assert.False(File.Exists(old));
        Assert.True(File.Exists(Path.Combine(Folder, Name)));
        Assert.True(File.Exists(Path.Combine(Folder, PendingUpdateStore.FileName)));
        UpdateDownloader.CleanOld(Folder, TimeSpan.FromDays(2));
        Assert.False(File.Exists(Path.Combine(Folder, Name)));
    }
}
