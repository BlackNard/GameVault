using GameVault.Core;

namespace GameVault.Infrastructure.Storage;

public sealed partial class AppDataStore
{
    private string ObsidianSettingsFile => Path.Combine(paths.Data, "obsidian.json");
    private string ObsidianRecordFile(Guid id) => Path.Combine(Folder(id), "obsidian-export.json");

    public Task<StoredDocument<ObsidianSettings>> LoadObsidianSettingsAsync() => Run(() =>
        documents.Read<ObsidianSettings>(ObsidianSettingsFile, ObsidianSettings.IsValid));

    public Task SaveObsidianSettingsAsync(ObsidianSettings settings) => Run(() =>
    {
        if (!ObsidianSettings.IsValid(settings)) throw new ArgumentException("Invalid Obsidian settings.", nameof(settings));
        EnsureRoots();
        JsonDocuments.RequireWritable(documents.Read<ObsidianSettings>(ObsidianSettingsFile, ObsidianSettings.IsValid));
        documents.Write(ObsidianSettingsFile, settings, ObsidianSettings.IsValid);
    });

    public Task<StoredDocument<ObsidianExportRecord>> LoadObsidianRecordAsync(Guid id) => Run(() =>
        documents.Read<ObsidianExportRecord>(ObsidianRecordFile(id), value => ObsidianExportRecord.IsValid(value, id)));

    /// <summary>Removed games keep their notes, so their record is written too.</summary>
    public Task SaveObsidianRecordAsync(ObsidianExportRecord record) => Run(() =>
    {
        ArgumentNullException.ThrowIfNull(record);
        var file = ObsidianRecordFile(record.GameId);
        if (LoadProfile(record.GameId).Profile.Value is null) throw new IOException("A readable game profile is required.");
        JsonDocuments.RequireWritable(documents.Read<ObsidianExportRecord>(file, value => ObsidianExportRecord.IsValid(value, record.GameId)));
        documents.Write(file, record, value => ObsidianExportRecord.IsValid(value, record.GameId));
    });
}
