using GameVault.Core;
using GameVault.Infrastructure.Capture;
using GameVault.Infrastructure.Storage;

namespace GameVault.Tests;

public sealed class NvidiaCaptureTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "GameVault nvidia", Guid.NewGuid().ToString("N"));
    private readonly ApplicationPaths paths;
    private readonly AppDataStore store;
    private readonly CaptureMediaStore media;
    private readonly string nvidia;
    private static readonly DateTimeOffset Start = new(2026, 11, 17, 20, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset End = Start.AddHours(1);
    private static readonly DateTimeOffset Now = new(2026, 11, 18, 0, 0, 0, TimeSpan.Zero);
    private GameProfile profile = new(1, Guid.NewGuid(), "TEKKEN 8", @"D:\Games\T8\t8.exe", @"D:\Games\T8", [], GameStatus.Playing, 20, false, false);
    private readonly Guid session = Guid.NewGuid();

    public NvidiaCaptureTests()
    {
        Directory.CreateDirectory(Path.Combine(root, "app"));
        paths = ApplicationPaths.FromRoot(Path.Combine(root, "app"));
        store = new AppDataStore(paths);
        media = new CaptureMediaStore(paths);
        nvidia = Path.Combine(root, "Videos", "NVIDIA");
        Directory.CreateDirectory(nvidia);
    }

    private async Task Seed(bool archived = false, bool withSession = true)
    {
        await store.InitializeAsync();
        await store.SaveProfileAsync(profile with { IsArchived = false });
        if (withSession) await store.SaveStatisticsAsync(new(1, profile.Id, [new(session, Start, End, 3600)], []));
        if (archived) await store.SaveProfileAsync(profile with { IsArchived = true });
    }
    private NvidiaCaptureImporter Importer() => new(paths, media);
    private static byte[] Mp4() => [0, 0, 0, 20, .. "ftyp"u8, .. "isom"u8, 0, 0, 2, 0, .. "isom"u8, 0, 0, 0, 8, .. "moov"u8, 0, 0, 0, 12, .. "mdat"u8, 1, 2, 3, 4];
    private static byte[] Png() => [137, 80, 78, 71, 13, 10, 26, 10, 0, 0, 0, 13, .. "IHDR"u8, 0, 0, 0, 1, 0, 0, 0, 1, 8, 6, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, .. "IEND"u8, 0, 0, 0, 0];
    private string Clip(string relative, byte[] bytes, DateTimeOffset written)
    {
        var path = Path.Combine(nvidia, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
        File.SetLastWriteTimeUtc(path, written.UtcDateTime);
        return path;
    }

    private sealed class FakeEnvironment : INvidiaEnvironment
    {
        public string ProgramFiles { get; set; } = @"C:\Program Files";
        public string ProgramFilesX86 { get; set; } = @"C:\Program Files (x86)";
        public string VideosFolder { get; set; } = @"C:\Users\Player\Videos";
        public HashSet<string> Directories { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> Processes { get; } = new(StringComparer.OrdinalIgnoreCase);
        public string? Registered { get; set; }
        public bool DirectoryExists(string path) => Directories.Contains(path);
        public bool IsProcessRunning(string processName) => Processes.Contains(processName);
        public string? RegisteredCaptureFolder() => Registered;
    }

    [Fact]
    public void Nothing_is_detected_without_nvidia_app_even_if_a_registry_folder_is_left_behind()
    {
        var info = NvidiaApp.Detect(new FakeEnvironment { Registered = @"D:\Old\NVIDIA" });
        Assert.Equal(NvidiaAppInfo.NotFound, info);
        Assert.False(NvidiaApp.UsesNvidia(CaptureSource.Auto, info));
    }

    [Theory]
    [InlineData(@"C:\Program Files\NVIDIA Corporation\NVIDIA App")]
    [InlineData(@"C:\Program Files (x86)\NVIDIA Corporation\NVIDIA App")]
    [InlineData(@"C:\Program Files\NVIDIA Corporation\NVIDIA GeForce Experience")]
    public void Installed_app_is_found_and_the_default_capture_folder_is_videos_nvidia(string folder)
    {
        var environment = new FakeEnvironment();
        environment.Directories.Add(folder);
        var info = NvidiaApp.Detect(environment);
        Assert.True(info.Installed);
        Assert.False(info.Running);
        Assert.Equal(@"C:\Users\Player\Videos\NVIDIA", info.CaptureFolder);
        Assert.True(NvidiaApp.UsesNvidia(CaptureSource.Auto, info));
    }

    [Fact]
    public void Running_overlay_and_the_configured_capture_folder_are_reported()
    {
        var environment = new FakeEnvironment { Registered = @"E:\Clips\Games" };
        environment.Directories.Add(@"C:\Program Files\NVIDIA Corporation\NVIDIA App");
        environment.Processes.Add("NVIDIA Overlay");
        var info = NvidiaApp.Detect(environment);
        Assert.True(info.Running);
        Assert.Equal(@"E:\Clips\Games", info.CaptureFolder);
    }

    [Fact]
    public void A_relative_or_blank_registry_path_is_ignored()
    {
        var environment = new FakeEnvironment { Registered = @"Clips\relative" };
        environment.Directories.Add(@"C:\Program Files\NVIDIA Corporation\NVIDIA App");
        Assert.Equal(@"C:\Users\Player\Videos\NVIDIA", NvidiaApp.Detect(environment).CaptureFolder);
    }

    [Theory]
    [InlineData(CaptureSource.Auto, false, false)]
    [InlineData(CaptureSource.Auto, true, true)]
    [InlineData(CaptureSource.GameVault, true, false)]
    [InlineData(CaptureSource.NvidiaApp, false, true)]
    public void Source_choice_decides_who_records(CaptureSource source, bool installed, bool expected) =>
        Assert.Equal(expected, NvidiaApp.UsesNvidia(source, new NvidiaAppInfo(installed, false, null)));

    [Fact]
    public async Task Clips_from_the_play_session_are_copied_once_and_the_originals_are_untouched()
    {
        await Seed();
        var video = Clip(@"TEKKEN 8\clip.mp4", Mp4(), Start.AddMinutes(30));
        var shot = Clip(@"TEKKEN 8\shot.png", Png(), Start.AddMinutes(10));
        var videoBytes = File.ReadAllBytes(video); var videoTime = File.GetLastWriteTimeUtc(video);

        var first = await Importer().ImportAsync(profile.Id, nvidia, Now);
        Assert.Equal(2, first.Imported);
        var listed = await media.ListAsync(profile.Id);
        Assert.Equal(2, listed.Length);
        Assert.All(listed, m => { Assert.Equal(CaptureMediaStatus.Complete, m.Status); Assert.Equal(session, m.SessionId); Assert.NotNull(m.SourceFingerprint); Assert.True(File.Exists(m.FilePath)); });
        Assert.Equal(videoBytes, File.ReadAllBytes(listed.Single(m => m.Kind == CaptureMediaKind.Video).FilePath));
        Assert.Contains(ProfileFolders.Resolve(paths, profile.Id), listed[0].FilePath);
        // Originals are only read.
        Assert.Equal(videoBytes, File.ReadAllBytes(video)); Assert.Equal(videoTime, File.GetLastWriteTimeUtc(video)); Assert.True(File.Exists(shot));

        var second = await Importer().ImportAsync(profile.Id, nvidia, Now);
        Assert.Equal(0, second.Imported); Assert.Equal(2, second.AlreadyImported);
        Assert.Equal(2, (await media.ListAsync(profile.Id)).Length);
    }

    [Fact]
    public async Task Files_outside_every_session_are_ignored_and_unsupported_formats_are_counted()
    {
        await Seed();
        Clip("before.mp4", Mp4(), Start.AddHours(-3));
        Clip("after.mp4", Mp4(), End.AddHours(2));
        Clip("photo.jxr", [1, 2, 3], Start.AddMinutes(5));
        Clip("inside.mp4", Mp4(), End.AddMinutes(5)); // NVIDIA finalises shortly after the session ends
        var result = await Importer().ImportAsync(profile.Id, nvidia, Now);
        Assert.Equal(1, result.Imported); Assert.Equal(1, result.Unsupported); Assert.Equal(0, result.Failed);
        Assert.Single(await media.ListAsync(profile.Id));
    }

    [Fact]
    public async Task A_file_written_moments_ago_or_still_open_is_left_for_a_later_run()
    {
        await Seed();
        var recent = Clip("recent.mp4", Mp4(), Now.AddSeconds(-2));
        File.SetLastWriteTimeUtc(recent, Now.AddSeconds(-2).UtcDateTime);
        await store.SaveStatisticsAsync(new(1, profile.Id, [new(session, Now.AddHours(-1), Now.AddSeconds(-1), 3600)], []));
        var busy = Clip("busy.mp4", Mp4(), Now.AddMinutes(-30));
        using var writer = new FileStream(busy, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var result = await Importer().ImportAsync(profile.Id, nvidia, Now);
        Assert.Equal(0, result.Imported); Assert.Equal(2, result.Waiting);
        Assert.Empty(await media.ListAsync(profile.Id));
    }

    [Fact]
    public async Task A_corrupt_clip_is_not_kept_and_does_not_stop_the_others()
    {
        await Seed();
        var bad = Clip("bad.mp4", [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18], Start.AddMinutes(5));
        Clip("good.mp4", Mp4(), Start.AddMinutes(6));
        var result = await Importer().ImportAsync(profile.Id, nvidia, Now);
        Assert.Equal(1, result.Imported); Assert.Equal(1, result.Failed); Assert.NotNull(result.FirstError);
        Assert.Single(await media.ListAsync(profile.Id));
        Assert.True(File.Exists(bad));
        var mediaFolder = Path.Combine(ProfileFolders.Resolve(paths, profile.Id), "Media");
        Assert.DoesNotContain(Directory.EnumerateFiles(mediaFolder, "*", SearchOption.AllDirectories), f => f.EndsWith(".partial") || f.EndsWith(".partial.json"));
    }

    [Fact]
    public async Task Nothing_happens_without_a_folder_or_recorded_sessions_and_archived_profiles_are_refused()
    {
        await Seed(withSession: false);
        Clip("clip.mp4", Mp4(), Start.AddMinutes(5));
        Assert.Equal(NvidiaImportResult.None, await Importer().ImportAsync(profile.Id, nvidia, Now));
        Assert.Equal(NvidiaImportResult.None, await Importer().ImportAsync(profile.Id, Path.Combine(root, "missing"), Now));
        await store.SaveStatisticsAsync(new(1, profile.Id, [new(session, Start, End, 3600)], []));
        await store.SaveProfileAsync(profile with { IsArchived = true });
        await Assert.ThrowsAsync<IOException>(() => Importer().ImportAsync(profile.Id, nvidia, Now));
    }

    [Fact]
    public async Task The_chosen_recorder_is_stored_per_game_and_defaults_to_auto()
    {
        await Seed();
        Assert.Equal(DocumentState.Missing, (await store.LoadCaptureSourceAsync(profile.Id)).State);
        await store.SaveCaptureSourceAsync(new(1, profile.Id, CaptureSource.NvidiaApp));
        var loaded = await new AppDataStore(paths).LoadCaptureSourceAsync(profile.Id);
        Assert.Equal(CaptureSource.NvidiaApp, loaded.Value!.Source);
        await store.SaveCaptureSourceAsync(new(1, profile.Id, CaptureSource.GameVault));
        Assert.Equal(CaptureSource.GameVault, (await store.LoadCaptureSourceAsync(profile.Id)).Value!.Source);
        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveCaptureSourceAsync(new(1, profile.Id, (CaptureSource)99)));
        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveCaptureSourceAsync(new(2, profile.Id, CaptureSource.Auto)));
    }

    [Fact]
    public async Task A_damaged_or_future_setting_is_read_only_and_never_overwritten()
    {
        await Seed();
        await store.SaveCaptureSourceAsync(new(1, profile.Id, CaptureSource.NvidiaApp));
        var file = Path.Combine(ProfileFolders.Resolve(paths, profile.Id), "capture-source.json");
        File.WriteAllText(file, "{\"schemaVersion\":99}");
        File.Delete(file + ".bak");
        Assert.NotEqual(DocumentState.Ready, (await store.LoadCaptureSourceAsync(profile.Id)).State);
        await Assert.ThrowsAnyAsync<Exception>(() => store.SaveCaptureSourceAsync(new(1, profile.Id, CaptureSource.GameVault)));
        Assert.Equal("{\"schemaVersion\":99}", File.ReadAllText(file));
    }

    public void Dispose() => Directory.Delete(root, recursive: true);
}
