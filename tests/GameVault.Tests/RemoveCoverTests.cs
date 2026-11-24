using GameVault.Core;
using GameVault.Infrastructure.Library;
using GameVault.Infrastructure.Storage;

namespace GameVault.Tests;

public sealed class RemoveCoverTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "GameVault cover removal", Guid.NewGuid().ToString("N"));
    private readonly LibraryService service;
    private readonly string exe;
    private static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jRZkAAAAASUVORK5CYII=");

    public RemoveCoverTests()
    {
        Directory.CreateDirectory(root);
        var paths = ApplicationPaths.FromRoot(root);
        service = new(paths, new AppDataStore(paths));
        var install = Path.Combine(root, "Game");
        Directory.CreateDirectory(install);
        exe = Path.Combine(install, "game.exe");
        File.WriteAllText(exe, "fixture only; never executed");
    }

    public void Dispose() { try { Directory.Delete(root, true); } catch (IOException) { } }

    private async Task<Guid> AddWithCoverAsync() => (await service.SaveAsync(new(null, "My game", exe, "", []), Png)).Profile.Id;
    private async Task<LibraryGame> ReadAsync(Guid id) => (await service.ListAsync()).Games.Single(g => g.Id == id);

    [Fact]
    public async Task RemovingTheCoverDeletesItAndKeepsTheRestOfTheProfile()
    {
        var id = await AddWithCoverAsync();
        Assert.NotNull((await ReadAsync(id)).CoverFile);
        await service.SaveAsync(new(id, "Renamed", exe, "", [], GameStatus.Completed, RemoveCover: true));
        var game = await ReadAsync(id);
        Assert.Null(game.CoverFile);
        Assert.Equal("Renamed", game.Profile!.Name);
        Assert.Equal(GameStatus.Completed, game.Profile.Status);
    }

    [Fact]
    public async Task AnAutomaticLookupNeverBringsARemovedCoverBack()
    {
        var id = await AddWithCoverAsync();
        await service.SaveAsync(new(id, "My game", exe, "", [], RemoveCover: true));
        Assert.False(await service.SetCoverIfMissingAsync(id, Png));
        Assert.Null((await ReadAsync(id)).CoverFile);
    }

    [Fact]
    public async Task AskingForInformationAgainAllowsTheCoverToBeFetchedAgain()
    {
        var id = await AddWithCoverAsync();
        await service.SaveAsync(new(id, "My game", exe, "", [], RemoveCover: true));
        Assert.False(await service.SetCoverIfMissingAsync(id, Png));
        await service.AllowCoverAgainAsync(id);
        Assert.True(await service.SetCoverIfMissingAsync(id, Png));
        Assert.NotNull((await ReadAsync(id)).CoverFile);
    }

    [Fact]
    public async Task ChoosingANewCoverClearsTheRemovedChoice()
    {
        var id = await AddWithCoverAsync();
        await service.SaveAsync(new(id, "My game", exe, "", [], RemoveCover: true));
        await service.SaveAsync(new(id, "My game", exe, "", []), Png);
        Assert.NotNull((await ReadAsync(id)).CoverFile);
        Assert.False(File.Exists(Path.Combine(root, "Profiles", "My game", LibraryService.CoverRemovedMarkerName)));
    }

    [Fact]
    public async Task SavingWithoutTheRemoveChoiceKeepsTheCover()
    {
        var id = await AddWithCoverAsync();
        await service.SaveAsync(new(id, "My game", exe, "", []));
        Assert.NotNull((await ReadAsync(id)).CoverFile);
    }
}
