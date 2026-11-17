using GameVault.Core;
using GameVault.Infrastructure.Capture;
using GameVault.Infrastructure.Storage;
namespace GameVault.Tests;

public sealed class RecordingHealthTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 17, 21, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_recorder_that_cannot_count_frames_never_raises_an_alarm()
    {
        var dog = new RecordingWatchdog();
        for (var second = 0; second < 60; second++) Assert.Equal(VideoHealth.Unknown, dog.Observe(-1, T0.AddSeconds(second)));
    }

    [Fact]
    public void Watching_starts_only_after_the_first_frame()
    {
        var dog = new RecordingWatchdog();
        for (var second = 0; second < 30; second++)
        {
            var health = dog.Observe(0, T0.AddSeconds(second));
            Assert.False(health.Stalled); Assert.False(health.Monitoring); Assert.Equal(0, health.Episodes);
        }
    }

    [Fact]
    public void Steady_frames_are_healthy_however_long_the_recording()
    {
        var dog = new RecordingWatchdog();
        for (var second = 0; second < 600; second++)
        {
            var health = dog.Observe(second * 60 + 1, T0.AddSeconds(second));
            Assert.False(health.Stalled); Assert.Equal(0, health.Episodes); Assert.Null(health.Warning);
        }
    }

    [Fact]
    public void A_picture_that_stops_is_reported_after_the_threshold_and_again_when_it_returns()
    {
        var dog = new RecordingWatchdog(TimeSpan.FromSeconds(4));
        dog.Observe(100, T0);
        Assert.False(dog.Observe(100, T0.AddSeconds(3)).Stalled);          // not long enough yet
        var stalled = dog.Observe(100, T0.AddSeconds(5));
        Assert.True(stalled.Stalled); Assert.Equal(1, stalled.Episodes);
        Assert.Equal(5, stalled.CurrentStall.TotalSeconds, 1);              // counted from the last frame, not from detection
        var long_ = dog.Observe(100, T0.AddSeconds(141));
        Assert.True(long_.Stalled); Assert.Equal(1, long_.Episodes); Assert.Equal(141, long_.TotalStalled.TotalSeconds, 1);
        var resumed = dog.Observe(160, T0.AddSeconds(142));
        Assert.False(resumed.Stalled); Assert.Equal(1, resumed.Episodes); Assert.Equal(142, resumed.TotalStalled.TotalSeconds, 1);
        Assert.Contains("1 time(s)", resumed.Warning);
        // A second freeze is a second episode.
        Assert.True(dog.Observe(160, T0.AddSeconds(148)).Stalled);
        Assert.Equal(2, dog.Observe(160, T0.AddSeconds(149)).Episodes);
    }

    [Fact]
    public void Short_gaps_below_the_threshold_are_ignored()
    {
        var dog = new RecordingWatchdog(TimeSpan.FromSeconds(4));
        long frames = 1;
        for (var second = 0; second < 100; second++)
        {
            if (second % 3 != 0) frames += 60;          // a 2-3 s hiccup now and then (the sporadic gaps seen after the freeze)
            Assert.Equal(0, dog.Observe(frames, T0.AddSeconds(second)).Episodes);
        }
    }

    // ---- coordinator integration ----
    private readonly string root = Path.Combine(Path.GetTempPath(), "gv-health-" + Guid.NewGuid().ToString("N"));
    private readonly Guid game = Guid.NewGuid();
    private readonly CaptureTarget target = new(Guid.NewGuid(), 123, 456);

    private async Task<CaptureCoordinator> Create(ICaptureBackend backend)
    {
        Directory.CreateDirectory(root);
        var paths = ApplicationPaths.FromRoot(root); var store = new AppDataStore(paths);
        await store.InitializeAsync();
        await store.SaveProfileAsync(new(1, game, "Test", Path.Combine(root, "test.exe"), root, [], GameStatus.Playing, 20, false, false));
        var now = DateTimeOffset.UtcNow;
        await store.SaveSessionJournalAsync(new(1, game, target.SessionId, now, now, 0, SessionState.Active, target.ProcessId, target.ProcessCreatedUtcTicks, Path.Combine(root, "test.exe"), null, 0));
        return new(paths, () => backend, TimeSpan.FromMilliseconds(500));
    }

    private sealed class Recorder(bool countsFrames) : ICaptureBackend, ICaptureBackendHealth
    {
        public long Frames;
        public long VideoFrames => countsFrames ? Frames : -1;
        public Task ScreenshotAsync(CaptureTarget t, string p, CancellationToken ct) => throw new NotSupportedException();
        public Task StartAsync(CaptureTarget t, string path, CaptureOptions o, CancellationToken ct)
        {
            File.WriteAllBytes(path, [0, 0, 0, 16, 102, 116, 121, 112, 105, 115, 111, 109, 0, 0, 0, 0, 0, 0, 0, 8, 109, 111, 111, 118, 0, 0, 0, 9, 109, 100, 97, 116, 1]);
            return Task.CompletedTask;
        }
        public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
    }

    [Fact]
    public async Task A_frozen_recording_is_flagged_live_and_the_saved_clip_carries_the_warning()
    {
        var backend = new Recorder(true);
        var coordinator = await Create(backend);
        await coordinator.StartAsync(game, target);
        backend.Frames = 10; coordinator.PollHealth(T0);
        backend.Frames = 70; coordinator.PollHealth(T0.AddSeconds(1));
        Assert.False(coordinator.VideoHealth.Stalled);
        coordinator.PollHealth(T0.AddSeconds(7));
        Assert.True(coordinator.VideoHealth.Stalled);
        var media = await coordinator.StopAsync();
        Assert.NotNull(media!.Warning);
        var listed = Assert.Single(await coordinator.ListMediaAsync(game));
        Assert.Equal(media.Warning, listed.Warning);
        Assert.Equal(VideoHealth.Unknown, coordinator.VideoHealth); // reset for the next recording
    }

    [Fact]
    public async Task A_healthy_recording_has_no_warning_and_a_backend_without_counts_is_never_flagged()
    {
        foreach (var counts in new[] { true, false })
        {
            var backend = new Recorder(counts);
            var coordinator = await Create(backend);
            await coordinator.StartAsync(game, target);
            for (var second = 0; second < 30; second++) { backend.Frames = 1 + second * 60; coordinator.PollHealth(T0.AddSeconds(second)); }
            Assert.False(coordinator.VideoHealth.Stalled);
            Assert.Null((await coordinator.StopAsync())!.Warning);
            Dispose();
        }
    }

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
