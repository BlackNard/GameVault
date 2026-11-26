using GameVault.Core;

namespace GameVault.Infrastructure.Storage;

public sealed partial class AppDataStore
{
    private string SteamLinkFile(Guid id) => Path.Combine(Folder(id), "steam-link.json");

    /// <summary>The game's link to Steam. A missing file means not linked.</summary>
    public Task<StoredDocument<SteamLink>> LoadSteamLinkAsync(Guid id) => Run(() =>
        documents.Read<SteamLink>(SteamLinkFile(id), value => SteamLink.IsValid(value, id)));

    public Task SaveSteamLinkAsync(SteamLink link) => Run(() =>
    {
        ArgumentNullException.ThrowIfNull(link);
        RequireLibraryGame(link.GameId);
        documents.Write(SteamLinkFile(link.GameId), link, value => SteamLink.IsValid(value, link.GameId));
    });

    /// <summary>Unlinks: the Steam figure is deleted together with its previous copy. Recorded sessions are not touched.</summary>
    public Task RemoveSteamLinkAsync(Guid id) => Run(() =>
        documents.Delete<SteamLink>(SteamLinkFile(id), value => SteamLink.IsValid(value, id)));

    private void RequireLibraryGame(Guid id)
    {
        var game = LoadProfile(id);
        if (game.Profile.State != DocumentState.Ready || game.Profile.Value!.IsArchived)
            throw new IOException("A writable game in your library is required for this.");
    }
}
