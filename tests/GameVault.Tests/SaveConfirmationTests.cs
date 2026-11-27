using GameVault.Core;
using GameVault.Infrastructure.Saves;
using GameVault.Infrastructure.Storage;

namespace GameVault.Tests;

/// <summary>A save folder GameVault found but may not use yet must be asked for plainly, not reported as a broken backup.</summary>
public sealed class SaveConfirmationTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "GameVault confirm", Guid.NewGuid().ToString("N"));
    private readonly Guid id = Guid.NewGuid();
    private ApplicationPaths Paths => ApplicationPaths.FromRoot(Path.Combine(root, "app"));
    private AppDataStore Store => new(Paths);
    // A folder inside the game's own folder is a likely save folder but not a certain one, so the player is asked.
    private string Found => Path.Combine(root, "game", "saves");
    private SaveDiscoveryService Discovery => new(Paths, Store, new SaveDiscoveryEnvironment(Path.Combine(root, "user"), Path.Combine(root, "docs"),
        Path.Combine(root, "saved"), Path.Combine(root, "roam"), Path.Combine(root, "local"), Path.Combine(root, "public"), []));
    private BackupService Backups(SaveDiscoveryService discovery) => new(Paths, Store, discovery, new(), _ => Task.FromResult(false));

    public void Dispose() { try { Directory.Delete(root, true); } catch (IOException) { } }

    private async Task SeedAsync()
    {
        Directory.CreateDirectory(Paths.Root);
        Directory.CreateDirectory(Path.Combine(root, "game"));
        File.WriteAllText(Path.Combine(root, "game", "game.exe"), "fixture only; never executed");
        Directory.CreateDirectory(Found);
        File.WriteAllText(Path.Combine(Found, "slot.sav"), "save");
        await Store.InitializeAsync();
        await Store.SaveProfileAsync(new(1, id, "Example", Path.Combine(root, "game", "game.exe"), Path.Combine(root, "game"), [], GameStatus.Playing, 20, false, false));
    }

    [Fact]
    public async Task AFoundButUnconfirmedFolderAsksForConfirmationInsteadOfReportingABrokenBackup()
    {
        await SeedAsync();
        var discovery = Discovery;
        var result = await Backups(discovery).BackUpAsync(id, true);
        Assert.Equal(BackupPhase.Failed, result.Phase);                 // nothing was backed up, so the alert stays
        Assert.Equal(SaveConfirmation.Message, result.Message);
        Assert.True(SaveConfirmation.IsNeeded(result.Message));
        Assert.StartsWith("Confirm the save folder.", (await discovery.ReadAsync(id)).Summary);
        Assert.Equal("Example: Confirm the save folder", ToastText.Split("Example: " + result.Message).Title);
        Assert.Equal(SaveConfirmation.Message, (await Backups(discovery).ReadAsync(id)).Message);
    }

    [Fact]
    public async Task OnceConfirmedTheOldRequestIsNoLongerShownAndBackupWorks()
    {
        await SeedAsync();
        var discovery = Discovery;
        await Backups(discovery).BackUpAsync(id, true);
        var candidate = (await discovery.ReadAsync(id)).Document.Value!.Sources.Single(s => s.CanConfirm);
        await discovery.ConfirmAsync(id, candidate.Id);
        var shown = await Backups(discovery).ReadAsync(id);
        Assert.Equal(BackupPhase.None, shown.Phase);
        Assert.False(SaveConfirmation.IsNeeded(shown.Message));
        Assert.Equal(BackupPhase.Complete, (await Backups(discovery).BackUpAsync(id, true)).Phase);
    }

    [Fact]
    public async Task OtherProblemsAreStillReportedAsAFailedBackup()
    {
        await SeedAsync();
        Directory.Delete(Found, true);                                  // nothing readable to confirm
        var result = await Backups(Discovery).BackUpAsync(id, true);
        Assert.Equal(BackupPhase.Failed, result.Phase);
        Assert.StartsWith("Backup could not finish.", result.Message);
    }
}
