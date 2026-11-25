using GameVault.Core;
using GameVault.Infrastructure.Storage;

namespace GameVault.Tests;

public sealed class SplashRulesTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "GameVault splash", Guid.NewGuid().ToString("N"));

    public SplashRulesTests() => Directory.CreateDirectory(root);
    public void Dispose() { try { Directory.Delete(root, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }

    private static readonly DateTimeOffset Shown = new(2026, 11, 24, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void The_splash_stays_at_least_the_minimum_and_never_longer_than_that_once_loading_is_done()
    {
        Assert.Equal(SplashTiming.MinimumVisible, SplashTiming.RemainingDelay(Shown, Shown));
        Assert.Equal(TimeSpan.FromMilliseconds(700), SplashTiming.RemainingDelay(Shown, Shown.AddMilliseconds(500)));
        Assert.Equal(TimeSpan.Zero, SplashTiming.RemainingDelay(Shown, Shown.AddMilliseconds(1200)));
        Assert.Equal(TimeSpan.Zero, SplashTiming.RemainingDelay(Shown, Shown.AddSeconds(30)));           // a slow start adds no wait
        Assert.Equal(TimeSpan.FromSeconds(1), SplashTiming.RemainingDelay(Shown, Shown.AddSeconds(1), TimeSpan.FromSeconds(2)));
        Assert.Equal(TimeSpan.Zero, SplashTiming.RemainingDelay(Shown, Shown.AddSeconds(-5), TimeSpan.Zero));
        Assert.InRange(SplashTiming.MinimumVisible, TimeSpan.FromMilliseconds(500), TimeSpan.FromSeconds(3));
    }

    [Fact]
    public void Preferences_are_read_tolerantly_and_default_to_shown_midnight_and_animated()
    {
        Assert.Equal(SplashPreferences.Default, SplashPreferences.Parse(null));
        Assert.Equal(SplashPreferences.Default, SplashPreferences.Parse(""));
        Assert.Equal(SplashPreferences.Default, SplashPreferences.Parse("{ broken"));
        Assert.Equal(SplashPreferences.Default, SplashPreferences.Parse("[1,2]"));
        Assert.Equal(SplashPreferences.Default, SplashPreferences.Parse("{\"schemaVersion\":1}"));
        var full = SplashPreferences.Parse("{\"schemaVersion\":1,\"theme\":\"Vice City\",\"reduceMotion\":true,\"showSplash\":false}");
        Assert.Equal(new SplashPreferences("Vice City", true), full);      // an old "showSplash": false is ignored
        Assert.Equal("Midnight", SplashPreferences.Parse("{\"theme\":\"No such theme\"}").Theme);
        Assert.Equal("Midnight", SplashPreferences.Parse("{\"theme\":5}").Theme);
    }

    [Fact]
    public async Task The_setting_defaults_to_on_for_older_files_and_the_choice_to_turn_it_off_is_remembered()
    {
        var paths = ApplicationPaths.FromRoot(root);
        Directory.CreateDirectory(paths.Data);
        File.WriteAllText(Path.Combine(paths.Data, "settings.json"),
            "{\"schemaVersion\":1,\"theme\":\"Crimson\",\"defaultBackupLimit\":20,\"keepAllBackups\":false,\"reduceMotion\":false}");
        var first = await new AppDataStore(paths).InitializeAsync();
        Assert.Equal(DocumentState.Ready, first.Settings.State);
        Assert.True(first.Settings.Value!.ShowSplash);

        await new AppDataStore(paths).SaveSettingsAsync(first.Settings.Value with { ShowSplash = false });
        var text = await File.ReadAllTextAsync(Path.Combine(paths.Data, "settings.json"));
        var preferences = SplashPreferences.Parse(text);                                                 // the startup code reads the very same file
        Assert.Equal("Crimson", preferences.Theme);
        var reopened = await new AppDataStore(paths).InitializeAsync();
        Assert.False(reopened.Settings.Value!.ShowSplash);
        Assert.True(AppSettings.Default.ShowSplash);
    }
}
