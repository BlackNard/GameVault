using GameVault.Core;
namespace GameVault.Infrastructure.Storage;

public sealed partial class AppDataStore
{
    private string CaptureSourceFile(Guid id) => Path.Combine(Folder(id), "capture-source.json");
    private static bool ValidCaptureSource(CaptureSourceSettings value, Guid id) =>
        value.SchemaVersion == 1 && value.GameId == id && Enum.IsDefined(value.Source);

    /// <summary>Which recorder this game uses. A missing file means Auto (NVIDIA App when installed, otherwise GameVault's recorder).</summary>
    public Task<StoredDocument<CaptureSourceSettings>> LoadCaptureSourceAsync(Guid id) => Run(() =>
        documents.Read<CaptureSourceSettings>(CaptureSourceFile(id), value => ValidCaptureSource(value, id)));

    public Task SaveCaptureSourceAsync(CaptureSourceSettings settings) => Run(() =>
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!ValidCaptureSource(settings, settings.GameId)) throw new ArgumentException("Invalid capture source setting.", nameof(settings));
        var game = LoadProfile(settings.GameId);
        if (game.Profile.State != DocumentState.Ready || game.Profile.Value!.IsArchived)
            throw new IOException("A writable game in your library is required before changing its capture source.");
        var file = CaptureSourceFile(settings.GameId);
        JsonDocuments.RequireWritable(documents.Read<CaptureSourceSettings>(file, value => ValidCaptureSource(value, settings.GameId)));
        documents.Write(file, settings, value => ValidCaptureSource(value, settings.GameId));
    });
}
