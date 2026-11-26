using GameVault.Core;

namespace GameVault.Infrastructure.Storage;

public sealed partial class AppDataStore
{
    private string SteamLinkFile(Guid id) => Path.Combine(Folder(id), "steam-link.json");
    private string SessionOriginsFile(Guid id) => Path.Combine(Folder(id), "session-origins.json");

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

    public Task<StoredDocument<SessionOrigins>> LoadSessionOriginsAsync(Guid id) => Run(() =>
        documents.Read<SessionOrigins>(SessionOriginsFile(id), value => SessionOrigins.IsValid(value, id)));

    public Task RecordSessionOriginAsync(Guid id, Guid sessionId, bool throughSteam) => Run(() =>
    {
        RequireLibraryGame(id);
        var file = SessionOriginsFile(id);
        var stored = documents.Read<SessionOrigins>(file, value => SessionOrigins.IsValid(value, id));
        JsonDocuments.RequireWritable(stored);
        var origins = stored.Value ?? new SessionOrigins(1, id, [], []);
        if (origins.Steam.Contains(sessionId) || origins.Other.Contains(sessionId)) return;
        origins = throughSteam ? origins with { Steam = [.. origins.Steam, sessionId] } : origins with { Other = [.. origins.Other, sessionId] };
        documents.Write(file, origins, value => SessionOrigins.IsValid(value, id));
    });

    private void RequireLibraryGame(Guid id)
    {
        var game = LoadProfile(id);
        if (game.Profile.State != DocumentState.Ready || game.Profile.Value!.IsArchived)
            throw new IOException("A writable game in your library is required for this.");
    }
}
