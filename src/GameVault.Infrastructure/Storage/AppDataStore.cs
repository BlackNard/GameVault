using System.Collections.Concurrent;
using GameVault.Core;

namespace GameVault.Infrastructure.Storage;

public sealed class AppDataStore
{
    private static readonly ConcurrentDictionary<string, object> Gates = new(StringComparer.OrdinalIgnoreCase);
    private readonly ApplicationPaths paths;
    private readonly ManagedPaths managed;
    private readonly JsonDocuments documents;
    private readonly object gate;
    private string SettingsFile => Path.Combine(paths.Data, "settings.json");

    public AppDataStore(ApplicationPaths paths) : this(paths, null) { }

    internal AppDataStore(ApplicationPaths paths, Action<string>? beforeCommit)
    {
        ArgumentNullException.ThrowIfNull(paths);
        this.paths = paths;
        gate = Gates.GetOrAdd(paths.Root, _ => new object());
        managed = new(paths);
        documents = new(managed, beforeCommit);
    }

    public Task<AppDataSnapshot> InitializeAsync() => Run(() =>
    {
        EnsureRoots();
        var settings = documents.Read<AppSettings>(SettingsFile, StorageValidation.Settings);
        if (settings.State == DocumentState.Missing)
        {
            documents.Write(SettingsFile, AppSettings.Default, StorageValidation.Settings);
            settings = documents.Read<AppSettings>(SettingsFile, StorageValidation.Settings);
        }
        var games = new List<StoredGame>();
        var warnings = new List<string>();
        if (settings.Message is not null) warnings.Add(settings.Message);
        foreach (var folder in Directory.GetDirectories(paths.Profiles))
        {
            if (!Guid.TryParseExact(Path.GetFileName(folder), "N", out var id) || id == Guid.Empty)
            {
                warnings.Add("An unrecognized profile folder was left unchanged.");
                continue;
            }
            var game = LoadProfile(id);
            // An interrupted creation may have statistics but no profile commit marker.
            if (game.Profile.State == DocumentState.Missing)
            {
                warnings.Add("An incomplete profile folder was preserved for recovery.");
                continue;
            }
            games.Add(game);
            if (game.Profile.Message is not null) warnings.Add(game.Profile.Message);
            if (game.Statistics.Message is not null) warnings.Add(game.Statistics.Message);
            if (game.Statistics.State == DocumentState.Missing) warnings.Add("A profile has no statistics file; it was not silently recreated.");
        }
        return new AppDataSnapshot(settings, games.ToArray(), warnings.ToArray());
    });

    public Task SaveSettingsAsync(AppSettings settings) => Run(() =>
    {
        ArgumentNullException.ThrowIfNull(settings);
        EnsureRoots();
        documents.Write(SettingsFile, settings, StorageValidation.Settings);
    });

    public Task SaveProfileAsync(GameProfile profile) => Run(() =>
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (!StorageValidation.Profile(profile, profile.Id)) throw new ArgumentException("Invalid game profile.", nameof(profile));
        EnsureRoots();
        var folder = Folder(profile.Id);
        var profileFile = Path.Combine(folder, "profile.json");
        var current = documents.Read<GameProfile>(profileFile, p => StorageValidation.Profile(p, profile.Id));
        JsonDocuments.RequireWritable(current);
        managed.EnsureDirectory(folder);
        if (current.State == DocumentState.Missing)
        {
            var statisticsFile = Path.Combine(folder, "statistics.json");
            var statistics = documents.Read<GameStatistics>(statisticsFile, s => StorageValidation.Statistics(s, profile.Id));
            JsonDocuments.RequireWritable(statistics);
            if (statistics.State == DocumentState.Missing)
                documents.Write(statisticsFile, GameStatistics.Empty(profile.Id), s => StorageValidation.Statistics(s, profile.Id));
        }
        managed.EnsureDirectory(Path.Combine(folder, "Backups"));
        documents.Write(profileFile, profile, p => StorageValidation.Profile(p, profile.Id));
    });

    public Task<StoredGame> LoadProfileAsync(Guid id) => Run(() => LoadProfile(id));

    public Task SaveStatisticsAsync(GameStatistics statistics) => Run(() =>
    {
        ArgumentNullException.ThrowIfNull(statistics);
        if (!StorageValidation.Statistics(statistics, statistics.GameId)) throw new ArgumentException("Invalid game statistics.", nameof(statistics));
        var game = LoadProfile(statistics.GameId);
        if (game.Profile.State != DocumentState.Ready) throw new IOException("A valid game profile is required before saving statistics.");
        documents.Write(Path.Combine(Folder(statistics.GameId), "statistics.json"), statistics,
            s => StorageValidation.Statistics(s, statistics.GameId));
    });

    private StoredGame LoadProfile(Guid id)
    {
        var folder = Folder(id);
        return new(id,
            documents.Read<GameProfile>(Path.Combine(folder, "profile.json"), p => StorageValidation.Profile(p, id)),
            documents.Read<GameStatistics>(Path.Combine(folder, "statistics.json"), s => StorageValidation.Statistics(s, id)));
    }

    private string Folder(Guid id)
    {
        if (id == Guid.Empty) throw new ArgumentException("A non-empty game identifier is required.", nameof(id));
        return Path.Combine(paths.Profiles, id.ToString("N"));
    }

    private void EnsureRoots()
    {
        managed.EnsureDirectory(paths.Data);
        managed.EnsureDirectory(paths.Profiles);
    }

    private Task<T> Run<T>(Func<T> action) => Task.Run(() => { lock (gate) return action(); });
    private Task Run(Action action) => Task.Run(() => { lock (gate) action(); });
}
