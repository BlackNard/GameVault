using GameVault.Core;
using GameVault.Infrastructure.Launching;
using GameVault.Infrastructure.Storage;

namespace GameVault.Infrastructure.Metadata;

public sealed record BulkMetadataResult(int Games, int GamesUpdated, int FieldsFilled, string[] Problems, bool Cancelled);

/// <summary>Fetches and stores game information: builds the request from the profile, runs the resolver and saves the result (ticket #83).</summary>
public sealed class GameMetadataService(AppDataStore store, IReadOnlyList<IGameMetadataProvider> providers, IStoreGameLauncher? launcher = null)
{
    private readonly IStoreGameLauncher storeLauncher = launcher ?? new StoreGameLauncher();

    public IReadOnlyList<IGameMetadataProvider> Providers => providers;

    public Task<StoredDocument<GameMetadataDocument>> LoadAsync(Guid id) => store.LoadMetadataAsync(id);

    public async Task<MetadataSettings> LoadSettingsAsync()
    {
        var stored = await store.LoadMetadataSettingsAsync().ConfigureAwait(false);
        return stored.Value ?? MetadataSettings.Default;
    }

    public async Task<bool> SettingsReadOnlyAsync() => (await store.LoadMetadataSettingsAsync().ConfigureAwait(false)).IsReadOnly;

    public Task SaveSettingsAsync(MetadataSettings settings) => store.SaveMetadataSettingsAsync(settings);

    /// <summary>Which providers can supply each field, for the settings page.</summary>
    public IReadOnlyDictionary<MetadataField, string[]> CapableProviders() =>
        Enum.GetValues<MetadataField>().ToDictionary(f => f, f => providers.Where(p => p.AvailableFields.Contains(f)).Select(p => p.Id).ToArray());

    public async Task<MetadataRequest> BuildRequestAsync(Guid id, DateTimeOffset? lastSizeScan, bool sizeOnDisk, bool force)
    {
        var game = await store.LoadProfileAsync(id).ConfigureAwait(false);
        if (game.Profile.State != DocumentState.Ready || game.Profile.Value is not GameProfile profile || profile.IsArchived)
            throw new IOException("This game profile is missing, removed or read-only, so its information cannot be updated.");
        string? appId = null, installRoot = null, steamapps = null;
        try
        {
            var launch = await store.LoadLaunchSettingsAsync(id).ConfigureAwait(false);
            var plan = storeLauncher.Resolve(profile.ExecutablePath, launch.Value?.TargetExecutablePath);
            if (plan is not null)
            {
                installRoot = plan.InstallRoot;
                if (plan.Provider == StoreProvider.Steam && plan.StoreKey.StartsWith("Steam:", StringComparison.Ordinal))
                {
                    appId = plan.StoreKey[6..];
                    steamapps = Directory.GetParent(plan.InstallRoot)?.Parent?.FullName;
                }
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException) { }
        // A game that is not from a store: use its own folder, but never a drive root.
        installRoot ??= Path.GetDirectoryName(profile.ExecutablePath) is { Length: > 3 } folder && Path.GetPathRoot(folder) != folder ? folder : null;
        return new(id, profile.Name, profile.ExecutablePath, installRoot, appId, steamapps, lastSizeScan, sizeOnDisk, force);
    }

    /// <summary>Refreshes one game. Owner-typed values are never replaced; existing values are kept when "only fill empty" is on unless <paramref name="replaceExisting"/> is set.</summary>
    public async Task<MetadataResolution> RefreshAsync(Guid id, bool replaceExisting, CancellationToken cancellationToken)
    {
        var settings = await LoadSettingsAsync().ConfigureAwait(false);
        if (replaceExisting) settings = settings with { OnlyFillEmpty = false };
        var stored = await store.LoadMetadataAsync(id).ConfigureAwait(false);
        JsonDocuments.RequireWritable(stored);
        var existing = stored.Value ?? GameMetadataDocument.Empty(id);
        var request = await BuildRequestAsync(id, existing.LastSizeScan, settings.SizeOnDisk, replaceExisting).ConfigureAwait(false);
        request = request with { SteamAppId = request.SteamAppId ?? existing.SteamAppId };
        var result = await MetadataResolver.ResolveAsync(request, providers, settings, existing, cancellationToken, p => p.Id == SteamStoreProvider.ProviderId).ConfigureAwait(false);
        // A lookup that failed (offline, rate limited) must not mark the game as looked up, or the automatic pass would never retry it.
        var failedEmpty = result.FieldsFilled == 0 && result.Problems.Length > 0;
        if (!failedEmpty && (result.FieldsFilled > 0 || existing.LastRefresh is null)) await store.SaveMetadataAsync(result.Document).ConfigureAwait(false);
        return result;
    }

    /// <summary>Fills only the missing fields of every given game, reporting progress, and stops cleanly when cancelled.</summary>
    public async Task<BulkMetadataResult> FetchMissingAsync(IReadOnlyList<(Guid Id, string Name)> games, IProgress<(int Done, int Total, string Name)>? progress, CancellationToken cancellationToken, bool onlyNeverLookedUp = false)
    {
        var updated = 0;
        var filled = 0;
        var problems = new List<string>();
        var done = 0;
        try
        {
            foreach (var (id, name) in games)
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report((done, games.Count, name));
                try
                {
                    if (onlyNeverLookedUp && (await store.LoadMetadataAsync(id).ConfigureAwait(false)).Value?.LastRefresh is not null) { done++; continue; }
                    var result = await RefreshAsync(id, false, cancellationToken).ConfigureAwait(false);
                    if (result.FieldsFilled > 0) updated++;
                    filled += result.FieldsFilled;
                    problems.AddRange(result.Problems.Select(p => $"{name}: {p}"));
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException) { problems.Add($"{name}: {error.Message}"); }
                done++;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new(games.Count, updated, filled, [.. problems], true);
        }
        progress?.Report((done, games.Count, ""));
        return new(games.Count, updated, filled, [.. problems.Take(20)], false);
    }

    /// <summary>The Steam app id of the game (from its Steam install, or found earlier by exact title), or null. Used to fetch its cover.</summary>
    public async Task<string?> SteamAppIdAsync(Guid id)
    {
        try
        {
            var request = await BuildRequestAsync(id, null, true, false).ConfigureAwait(false);
            return request.SteamAppId ?? (await store.LoadMetadataAsync(id).ConfigureAwait(false)).Value?.SteamAppId;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException) { return null; }
    }

    /// <summary>The owner's own description. It is marked as typed by the owner, so no provider ever replaces it. Empty text removes it.</summary>
    public async Task SaveDescriptionAsync(Guid id, string? text)
    {
        var stored = await store.LoadMetadataAsync(id).ConfigureAwait(false);
        JsonDocuments.RequireWritable(stored);
        var document = stored.Value ?? GameMetadataDocument.Empty(id);
        var fields = new Dictionary<string, MetadataValue>(document.Fields);
        var clean = string.Join(' ', (text ?? "").Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries)).Trim();
        if (clean.Length == 0) fields.Remove(MetadataField.Description.ToString());
        else fields[MetadataField.Description.ToString()] = new("you", true, DateTimeOffset.UtcNow, Text: clean.Length > GameMetadataDocument.MaxTextLength ? clean[..GameMetadataDocument.MaxTextLength] : clean);
        await store.SaveMetadataAsync(document with { Fields = fields }).ConfigureAwait(false);
    }
}
