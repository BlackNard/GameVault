using System.Collections.Concurrent;
using GameVault.Core;

namespace GameVault.Infrastructure.Storage;

public sealed partial class AppDataStore
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
        foreach (var (folderId, _) in ProfileFolders.Enumerate(paths))
        {
            if (folderId is not Guid id)
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
            // Folders are named after the game. Older GUID-named folders are renamed once; a locked folder is retried next start.
            if (game.Profile.State == DocumentState.Ready && game.Profile.Value is { } profileValue)
                ProfileFolders.Normalize(paths, id, profileValue.Name);
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
        // A game without a folder yet gets one named after it (a duplicate name gets " (2)").
        if (!Directory.Exists(folder)) folder = ProfileFolders.ForNew(paths, profile.Name);
        var profileFile = Path.Combine(folder, "profile.json");
        var current = documents.Read<GameProfile>(profileFile, p => StorageValidation.Profile(p, profile.Id));
        JsonDocuments.RequireWritable(current);
        managed.EnsureDirectory(folder);
        ProfileFolders.Register(paths, profile.Id, folder);
        ProfileFolders.EnsureMarker(folder, profile.Id);
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
        // Renaming a game renames its folder; if the folder is in use it keeps the old name and is renamed on a later save or start.
        ProfileFolders.Normalize(paths, profile.Id, profile.Name);
    });

    public Task<StoredGame> LoadProfileAsync(Guid id) => Run(() => LoadProfile(id));

    public Task<StoredDocument<LaunchSettings>> LoadLaunchSettingsAsync(Guid id) => Run(() =>
        documents.Read<LaunchSettings>(Path.Combine(Folder(id), "launch.json"), value => ValidLaunchSettings(value, id)));

    public Task SaveLaunchSettingsAsync(LaunchSettings settings) => Run(() =>
    {
        ArgumentNullException.ThrowIfNull(settings);
        var game = LoadProfile(settings.GameId);
        if (game.Profile.State != DocumentState.Ready || game.Profile.Value!.IsArchived)
            throw new IOException("A writable game in your library is required before changing launch settings.");
        documents.Write(Path.Combine(Folder(settings.GameId), "launch.json"), settings,
            value => ValidLaunchSettings(value, settings.GameId));
    });

    private static bool ValidLaunchSettings(LaunchSettings value, Guid id) => value.SchemaVersion == 1 && value.GameId == id &&
        (value.TargetExecutablePath is null || (Path.IsPathFullyQualified(value.TargetExecutablePath) &&
            string.Equals(Path.GetExtension(value.TargetExecutablePath), ".exe", StringComparison.OrdinalIgnoreCase)));

    public Task SaveStatisticsAsync(GameStatistics statistics) => Run(() =>
    {
        ArgumentNullException.ThrowIfNull(statistics);
        if (!StorageValidation.Statistics(statistics, statistics.GameId)) throw new ArgumentException("Invalid game statistics.", nameof(statistics));
        var game = LoadProfile(statistics.GameId);
        if (game.Profile.State != DocumentState.Ready) throw new IOException("A valid game profile is required before saving statistics.");
        RequireCorrectionProjection(statistics,game);
        documents.Write(Path.Combine(Folder(statistics.GameId), "statistics.json"), statistics,
            s => StorageValidation.Statistics(s, statistics.GameId));
    });

    public Task<StoredSession[]> LoadSessionJournalsAsync(Guid gameId) => Run(() => LoadSessions(gameId));

    private StoredSession[] LoadSessions(Guid gameId)
    {
        var folder = Path.Combine(Folder(gameId), "Sessions");
        managed.Check(folder);
        if (!Directory.Exists(folder)) return Array.Empty<StoredSession>();
        var result = new List<StoredSession>();
        var files = Directory.GetFiles(folder).Where(file => file.EndsWith(".json", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".json.bak", StringComparison.OrdinalIgnoreCase))
            .Select(file => file.EndsWith(".bak", StringComparison.OrdinalIgnoreCase) ? file[..^4] : file).Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
        {
            if (!Guid.TryParseExact(Path.GetFileNameWithoutExtension(file), "N", out var id) || id == Guid.Empty)
            { result.Add(new(Guid.Empty, new(null, DocumentState.Corrupt, "An unrecognized session journal was preserved."))); continue; }
            result.Add(new(id, documents.Read<SessionJournal>(file, value => StorageValidation.Session(value, gameId, id))));
        }
        return result.ToArray();
    }

    public Task SaveSessionJournalAsync(SessionJournal journal) => Run(() =>
    {
        if (!StorageValidation.Session(journal, journal.GameId, journal.SessionId)) throw new ArgumentException("Invalid session checkpoint.");
        var game = LoadProfile(journal.GameId);
        if (game.Profile.State != DocumentState.Ready || game.Statistics.State != DocumentState.Ready)
            throw new IOException("The game profile/statistics need attention before playtime can be saved. Existing data was preserved.");
        var folder = Folder(journal.GameId);
        var file = Path.Combine(folder, "Sessions", journal.SessionId.ToString("N") + ".json");
        var previous = documents.Read<SessionJournal>(file, value => StorageValidation.Session(value, journal.GameId, journal.SessionId));
        JsonDocuments.RequireWritable(previous);
        if (previous.Value is SessionJournal old && (old.StartedAt != journal.StartedAt || old.DurationTicks > journal.DurationTicks ||
            old.ObservedAt > journal.ObservedAt || old.RecoveryCount > journal.RecoveryCount || (old.State == SessionState.Completed && journal.State != SessionState.Completed)))
            throw new IOException("This session checkpoint conflicts with newer saved data. Existing files were kept.");
        var session = new PlaySession(journal.SessionId, journal.StartedAt,
            journal.State == SessionState.Active ? null : journal.ObservedAt, journal.DurationTicks / TimeSpan.TicksPerSecond);
        var statistics = game.Statistics.Value!;
        var existing = statistics.Sessions.SingleOrDefault(s => s.Id == journal.SessionId);
        if (existing is not null && (existing.StartedAt != session.StartedAt || existing.DurationSeconds > session.DurationSeconds))
            throw new IOException("The session journal conflicts with recorded statistics. Neither was reset.");
        var updated = statistics with { Sessions = existing is null ? [.. statistics.Sessions, session] : statistics.Sessions.Select(s => s.Id == session.Id ? session : s).ToArray() };
        if (!StorageValidation.Statistics(updated, journal.GameId)) throw new IOException("The session could not be combined with the existing playtime totals.");
        RequireCorrectionProjection(updated,game);
        // The journal is authoritative. A crash before statistics commit is reconciled by session ID on startup.
        if (previous.Value != journal) documents.Write(file, journal, value => StorageValidation.Session(value, journal.GameId, journal.SessionId));
        if (existing != session)
            documents.Write(Path.Combine(folder, "statistics.json"), updated, value => StorageValidation.Statistics(value, journal.GameId));
    });

    private StoredGame LoadProfile(Guid id)
    {
        var folder = Folder(id);
        var profile=documents.Read<GameProfile>(Path.Combine(folder,"profile.json"),p=>StorageValidation.Profile(p,id));
        var statistics=documents.Read<GameStatistics>(Path.Combine(folder,"statistics.json"),s=>StorageValidation.Statistics(s,id));
        return new(id,profile,ReconcileCorrections(id,profile,statistics));
    }
    private string Folder(Guid id)
    {
        if (id == Guid.Empty) throw new ArgumentException("A non-empty game identifier is required.", nameof(id));
        return ProfileFolders.Resolve(paths, id);
    }

    private void EnsureRoots()
    {
        managed.EnsureDirectory(paths.Data);
        managed.EnsureDirectory(paths.Profiles);
    }

    private Task<T> Run<T>(Func<T> action) => Task.Run(() => { lock (gate) return action(); });
    private Task Run(Action action) => Task.Run(() => { lock (gate) action(); });
}
