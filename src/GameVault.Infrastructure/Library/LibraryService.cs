using GameVault.Core;
using GameVault.Infrastructure.Saves;
using GameVault.Infrastructure.Storage;
using System.Collections.Concurrent;

namespace GameVault.Infrastructure.Library;

public sealed class LibraryService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new(StringComparer.OrdinalIgnoreCase);
    private readonly ApplicationPaths paths;
    private readonly AppDataStore store;
    private readonly ManagedPaths managed;
    private readonly SemaphoreSlim gate;

    public LibraryService(ApplicationPaths paths, AppDataStore store)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(store);
        this.paths = paths;
        this.store = store;
        managed = new(paths);
        gate = Gates.GetOrAdd(paths.Root, _ => new SemaphoreSlim(1, 1));
    }

    public Task<LibrarySnapshot> ListAsync() => Task.Run(async () =>
    {
        var snapshot = await store.InitializeAsync().ConfigureAwait(false);
        var items = snapshot.Games.Select(game =>
        {
            var cover = Path.Combine(ProfileFolders.Resolve(paths, game.Id), "cover.png");
            string? warning = game.Profile.Message ?? game.Statistics.Message;
            try { managed.Check(cover); if (!File.Exists(cover)) cover = null; }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { cover = null; warning = "The cover file could not be accessed safely."; }
            var statistics = game.Statistics.Value;
            long? played = statistics is null ? null : statistics.Adjustments.Aggregate(
                statistics.Sessions.Aggregate(0L, (total, session) => checked(total + session.DurationSeconds)),
                (total, adjustment) => checked(total + adjustment.Seconds));
            DateTimeOffset? lastPlayed = statistics is { Sessions.Length: > 0 } ? statistics.Sessions.Max(session => session.EndedAt ?? session.StartedAt) : null;
            var hero = HeroFileOf(game.Id);
            return new LibraryGame(game.Id, game.Profile.Value, game.Profile.IsReadOnly || game.Profile.Value is null,
                game.Profile.Value is not null && !File.Exists(game.Profile.Value.ExecutablePath), cover, played, warning, lastPlayed, hero);
        }).OrderBy(g => g.Profile?.Name, StringComparer.OrdinalIgnoreCase).ThenBy(g => g.Id).ToArray();
        return new LibrarySnapshot(items, snapshot.Warnings);
    });

    public async Task<GameSaveResult> SaveAsync(GameDraft draft, byte[]? coverPng = null)
    {
        ArgumentNullException.ThrowIfNull(draft);
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var snapshot = await store.InitializeAsync().ConfigureAwait(false);
            var existing = draft.Id.HasValue ? snapshot.Games.SingleOrDefault(g => g.Id == draft.Id) : null;
            if (draft.Id.HasValue && (existing?.Profile.Value is null || existing.Profile.IsReadOnly))
                throw new IOException("This profile is missing or read-only. Its data has not been changed.");
            if (string.IsNullOrWhiteSpace(draft.Name)) throw new ArgumentException("Enter a game name.");
            var executable = Canonical(draft.ExecutablePath);
            if (!string.Equals(Path.GetExtension(executable), ".exe", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Choose a Windows game executable (.exe).");
            if (!File.Exists(executable) && (existing is null || !SamePath(executable, existing.Profile.Value!.ExecutablePath)))
                throw new ArgumentException("The game executable was not found. Browse to an existing .exe file.");
            var working = string.IsNullOrWhiteSpace(draft.WorkingDirectory) ? Path.GetDirectoryName(executable)! : Canonical(draft.WorkingDirectory);
            if (!Directory.Exists(working) && (existing is null || !SamePath(working, existing.Profile.Value!.WorkingDirectory)))
                throw new ArgumentException("The working directory was not found. Choose an existing folder.");
            var sources = draft.SaveSources?.Select(Canonical).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
                ?? throw new ArgumentException("Save locations must be a list of absolute paths.");
            new SaveSourcePolicy(paths,[Path.GetDirectoryName(executable)!,working]).ValidateSet(draft.SaveSources.Select(s=>s.Trim().Trim('"')));
            if (!Enum.IsDefined(draft.Status)) throw new ArgumentException("Choose a supported game status.");
            CheckDuplicate(snapshot, executable, draft.Id);
            if (coverPng is not null && (coverPng.Length < 33 || coverPng.Length > 10 * 1024 * 1024 ||
                !coverPng.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })))
                throw new ArgumentException("Choose a valid PNG/JPEG image. The normalized cover must be at most 10 MB.");
            GameProfile profile;
            if (existing?.Profile.Value is GameProfile previous)
                profile = previous with { Name = draft.Name.Trim(), ExecutablePath = executable, WorkingDirectory = working, SaveSources = sources, Status = draft.Status };
            else
            {
                if (snapshot.Settings.IsReadOnly || snapshot.Settings.Value is null) throw new IOException("Settings need attention before a new game can inherit their defaults.");
                profile = new(1, Guid.NewGuid(), draft.Name.Trim(), executable, working, sources, draft.Status,
                    snapshot.Settings.Value.DefaultBackupLimit, snapshot.Settings.Value.KeepAllBackups, false);
            }
            await store.SaveProfileAsync(profile).ConfigureAwait(false);
            string? warning = null;
            if (coverPng is not null)
            {
                try { WriteCover(profile.Id, coverPng); DeleteCoverRemovedMarker(profile.Id); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                { warning = "The game was saved, but its cover could not be updated. Edit the game to retry the cover."; }
            }
            else if (draft.RemoveCover)
            {
                try { RemoveCoverFiles(profile.Id); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                { warning = "The game was saved, but its cover could not be removed. Edit the game to retry."; }
            }
            return new(profile, warning);
        }
        finally { gate.Release(); }
    }

    /// <summary>Writes a cover only when the game has none. Never replaces a cover the owner chose. Returns whether a cover was written.</summary>
    public async Task<bool> SetCoverIfMissingAsync(Guid id, byte[] png)
    {
        ArgumentNullException.ThrowIfNull(png);
        if (png.Length < 33 || png.Length > 10 * 1024 * 1024 || !png.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
            throw new ArgumentException("The cover must be a valid PNG of at most 10 MB.");
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var game = await store.LoadProfileAsync(id).ConfigureAwait(false);
            if (game.Profile.IsReadOnly || game.Profile.Value is not { IsArchived: false }) return false;
            if (File.Exists(Path.Combine(ProfileFolders.Resolve(paths, id), "cover.png")) || File.Exists(CoverRemovedMarker(id))) return false;   // a removed cover stays removed
            WriteCover(id, png);
            return true;
        }
        finally { gate.Release(); }
    }

    public async Task<GameSaveResult> RestoreAsync(Guid id)
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var snapshot = await store.InitializeAsync().ConfigureAwait(false);
            var game = snapshot.Games.SingleOrDefault(g => g.Id == id);
            if (game?.Profile.Value is not GameProfile profile || game.Profile.IsReadOnly)
                throw new IOException("This profile is missing or read-only; it was left unchanged.");
            CheckDuplicate(snapshot, profile.ExecutablePath, id);
            var restored = profile with { IsArchived = false };
            if (profile.IsArchived) await store.SaveProfileAsync(restored).ConfigureAwait(false);
            return new(restored, null);
        }
        finally { gate.Release(); }
    }

    public async Task SetArchivedAsync(Guid id, bool archived)
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var snapshot = await store.InitializeAsync().ConfigureAwait(false);
            var game = snapshot.Games.SingleOrDefault(g => g.Id == id);
            if (game?.Profile.Value is not GameProfile profile || game.Profile.IsReadOnly)
                throw new IOException("This profile is missing or read-only; it was left unchanged.");
            if (!archived) CheckDuplicate(snapshot, profile.ExecutablePath, id);
            await store.SaveProfileAsync(profile with { IsArchived = archived }).ConfigureAwait(false);
        }
        finally { gate.Release(); }
    }

    private static void CheckDuplicate(AppDataSnapshot snapshot, string path, Guid? except)
    {
        var match = snapshot.Games.FirstOrDefault(g => g.Id != except && g.Profile.Value is not null && SamePath(path, g.Profile.Value.ExecutablePath));
        if (match is not null) throw new DuplicateGameException(match.Id, match.Profile.Value!.IsArchived);
    }

    public async Task MarkPlayingAsync(Guid id)
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var game = await store.LoadProfileAsync(id).ConfigureAwait(false);
            if (game.Profile.IsReadOnly || game.Profile.Value is not GameProfile profile || profile.IsArchived)
                throw new IOException("The game started, but its profile could not be updated safely.");
            if (profile.Status == GameStatus.NotStarted)
                await store.SaveProfileAsync(profile with { Status = GameStatus.Playing }).ConfigureAwait(false);
        }
        finally { gate.Release(); }
    }

    private static string Canonical(string path)
    {
        path = path?.Trim().Trim('"') ?? "";
        if (!Path.IsPathFullyQualified(path)) throw new ArgumentException("Choose or enter a full absolute path.");
        try { return Path.GetFullPath(path); }
        catch (Exception error) when (error is ArgumentException or NotSupportedException)
        { throw new ArgumentException("The selected path is invalid.", error); }
    }

    private static bool SamePath(string first, string second) => string.Equals(Canonical(first), Canonical(second), StringComparison.OrdinalIgnoreCase);

    private static readonly string[] HeroNames = ["hero.jpg", "hero.png"];

    private string? HeroFileOf(Guid id)
    {
        try
        {
            var folder = ProfileFolders.Resolve(paths, id);
            foreach (var name in HeroNames)
            {
                var file = Path.Combine(folder, name);
                managed.Check(file);
                if (File.Exists(file)) return file;
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        return null;
    }

    /// <summary>Saves the wide background image of a game (JPEG or PNG, at most 8 MB) only when it has none. Returns whether it was written.</summary>
    public async Task<bool> SetHeroIfMissingAsync(Guid id, byte[] image)
    {
        ArgumentNullException.ThrowIfNull(image);
        var jpeg = image.Length > 3 && image[0] == 0xFF && image[1] == 0xD8 && image[2] == 0xFF;
        var png = image.Length > 8 && image.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        if (image.Length > 8 * 1024 * 1024 || !(jpeg || png)) throw new ArgumentException("The background must be a JPEG or PNG image of at most 8 MB.");
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var game = await store.LoadProfileAsync(id).ConfigureAwait(false);
            if (game.Profile.IsReadOnly || game.Profile.Value is not { IsArchived: false } || HeroFileOf(id) is not null) return false;
            WriteProfileFile(id, jpeg ? "hero.jpg" : "hero.png", image);
            return true;
        }
        finally { gate.Release(); }
    }

    /// <summary>Removes the background image, for example because the owner chose a different game and the old picture belongs to the wrong one.</summary>
    public async Task ClearHeroAsync(Guid id)
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (HeroFileOf(id) is { } file) File.Delete(file);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        finally { gate.Release(); }
    }

    private void WriteCover(Guid id, byte[] png) => WriteProfileFile(id, "cover.png", png);

    /// <summary>The player asked for information again (Refresh info): a cover they removed earlier may be fetched again, so the "removed" choice is forgotten.</summary>
    public async Task AllowCoverAgainAsync(Guid id)
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try { DeleteCoverRemovedMarker(id); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        finally { gate.Release(); }
    }

    /// <summary>Remembers that the player removed this game's cover, so the automatic cover lookup does not bring it back.</summary>
    public const string CoverRemovedMarkerName = "cover-removed.marker";

    private string CoverRemovedMarker(Guid id) => Path.Combine(ProfileFolders.Resolve(paths, id), CoverRemovedMarkerName);

    private void DeleteCoverRemovedMarker(Guid id)
    {
        var marker = CoverRemovedMarker(id);
        managed.Check(marker);
        if (File.Exists(marker)) File.Delete(marker);
    }

    /// <summary>Deletes the cover (and its previous copy) and leaves the marker. Only this game's cover files are touched.</summary>
    private void RemoveCoverFiles(Guid id)
    {
        var cover = Path.Combine(ProfileFolders.Resolve(paths, id), "cover.png");
        managed.Check(cover); managed.Check(cover + ".bak");
        if (File.Exists(cover)) File.Delete(cover);
        if (File.Exists(cover + ".bak")) File.Delete(cover + ".bak");
        var marker = CoverRemovedMarker(id);
        managed.Check(marker);
        if (!File.Exists(marker)) File.WriteAllText(marker, "The player removed this game's cover on purpose; automatic lookups must not add one.");
    }

    private void WriteProfileFile(Guid id, string name, byte[] png)
    {
        var file = Path.Combine(ProfileFolders.Resolve(paths, id), name);
        managed.Check(file); managed.Check(file + ".bak");
        var temporary = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            { stream.Write(png); stream.Flush(true); }
            managed.Check(file); managed.Check(file + ".bak");
            if (File.Exists(file)) File.Replace(temporary, file, file + ".bak");
            else File.Move(temporary, file, overwrite: false);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
