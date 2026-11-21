using GameVault.Core;
namespace GameVault.Infrastructure.Storage;

public sealed partial class AppDataStore
{
    private string MetadataFile(Guid id) => Path.Combine(Folder(id), "metadata.json");
    private string MetadataSettingsFile => Path.Combine(paths.Data, "metadata-settings.json");

    /// <summary>Facts about a game (description, release date, developers, genres, size). A missing file means nothing has been fetched yet.</summary>
    public Task<StoredDocument<GameMetadataDocument>> LoadMetadataAsync(Guid id) => Run(() =>
        documents.Read<GameMetadataDocument>(MetadataFile(id), value => GameMetadataDocument.IsValid(value, id)));

    public Task SaveMetadataAsync(GameMetadataDocument metadata) => Run(() =>
    {
        ArgumentNullException.ThrowIfNull(metadata);
        if (!GameMetadataDocument.IsValid(metadata, metadata.GameId)) throw new ArgumentException("Invalid game information.", nameof(metadata));
        var game = LoadProfile(metadata.GameId);
        if (game.Profile.State != DocumentState.Ready || game.Profile.Value!.IsArchived)
            throw new IOException("A writable game in your library is required before saving game information.");
        var file = MetadataFile(metadata.GameId);
        JsonDocuments.RequireWritable(documents.Read<GameMetadataDocument>(file, value => GameMetadataDocument.IsValid(value, metadata.GameId)));
        documents.Write(file, metadata, value => GameMetadataDocument.IsValid(value, metadata.GameId));
    });

    public Task<StoredDocument<MetadataSettings>> LoadMetadataSettingsAsync() => Run(() =>
        documents.Read<MetadataSettings>(MetadataSettingsFile, MetadataSettings.IsValid));

    public Task SaveMetadataSettingsAsync(MetadataSettings settings) => Run(() =>
    {
        if (!MetadataSettings.IsValid(settings)) throw new ArgumentException("Invalid game information settings.", nameof(settings));
        EnsureRoots();
        JsonDocuments.RequireWritable(documents.Read<MetadataSettings>(MetadataSettingsFile, MetadataSettings.IsValid));
        documents.Write(MetadataSettingsFile, settings, MetadataSettings.IsValid);
    });
}
