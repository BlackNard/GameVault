using System.Text.Json;
using GameVault.Core;
using GameVault.Infrastructure.Storage;

namespace GameVault.Tests;

public sealed class WelcomeTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "GameVault welcome", Guid.NewGuid().ToString("N"));

    public WelcomeTests() => Directory.CreateDirectory(root);
    public void Dispose() { try { Directory.Delete(root, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }

    [Theory]
    [InlineData(0, false, false, true)]      // a brand-new library
    [InlineData(0, false, true, false)]      // the player skipped it
    [InlineData(0, true, false, false)]      // looking at removed profiles
    [InlineData(1, false, false, false)]     // there is a game
    [InlineData(5, true, false, false)]
    [InlineData(2, false, true, false)]
    public void The_welcome_shows_only_for_a_library_that_never_had_a_game_and_was_not_skipped(int games, bool removedView, bool dismissed, bool expected) =>
        Assert.Equal(expected, WelcomeRules.ShouldShow(games, removedView, dismissed));

    [Fact]
    public void A_removed_only_library_does_not_get_the_welcome_because_games_are_stored()
        => Assert.False(WelcomeRules.ShouldShow(storedGames: 3, showingRemovedProfiles: false, dismissed: false));

    [Fact]
    public async Task Skipping_is_remembered_and_older_settings_files_without_the_field_still_load()
    {
        var paths = ApplicationPaths.FromRoot(root);
        Directory.CreateDirectory(paths.Data);
        // an older file written before the field existed
        File.WriteAllText(Path.Combine(paths.Data, "settings.json"),
            "{\"schemaVersion\":1,\"theme\":\"Midnight\",\"defaultBackupLimit\":20,\"keepAllBackups\":false,\"reduceMotion\":false,\"closeToTray\":true,\"notifyBackupFailures\":true}");
        var first = await new AppDataStore(paths).InitializeAsync();
        Assert.Equal(DocumentState.Ready, first.Settings.State);
        Assert.False(first.Settings.Value!.WelcomeDismissed);

        await new AppDataStore(paths).SaveSettingsAsync(first.Settings.Value with { WelcomeDismissed = true });
        var reopened = await new AppDataStore(paths).InitializeAsync();
        Assert.True(reopened.Settings.Value!.WelcomeDismissed);
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(paths.Data, "settings.json")));
        Assert.True(json.RootElement.GetProperty("welcomeDismissed").GetBoolean());
        Assert.False(AppSettings.Default.WelcomeDismissed);
    }
}
