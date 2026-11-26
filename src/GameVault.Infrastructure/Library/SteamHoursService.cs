using GameVault.Core;
using GameVault.Infrastructure.Launching;
using GameVault.Infrastructure.Storage;

namespace GameVault.Infrastructure.Library;

public sealed record SteamAccountPlaytime(SteamAccount Account, long Minutes);

/// <summary>A Steam game this profile may be linked to. <see cref="Proven"/>: the program is inside its install folder.</summary>
public sealed record SteamOffer(string AppId, string SteamName, bool Proven, SteamAppSummary? Summary, SteamAccountPlaytime[] Accounts);

public sealed record SteamOfferResult(SteamOffer? Offer, string? Reason);

/// <summary><see cref="Totals"/> is set only while the link is <see cref="Active"/>.</summary>
public sealed record SteamHoursState(SteamLink? Link, bool Active, PlaytimeTotals? Totals, string? Message);

/// <summary>Links a profile to the Steam game it really is and adds Steam's hours to it (ticket #62). Recorded sessions are never changed.</summary>
public sealed class SteamHoursService(AppDataStore store, SteamLocalData steam, IStoreGameLauncher launcher,
    Func<string, CancellationToken, Task<SteamAppSummary?>> summary)
{
    public async Task<SteamHoursState> GetAsync(Guid id)
    {
        var stored = await store.LoadSteamLinkAsync(id).ConfigureAwait(false);
        if (stored.Value is not { } link) return new(null, false, null, stored.Message);
        var game = await store.LoadProfileAsync(id).ConfigureAwait(false);
        if (game.Profile.Value is not { } profile || game.Statistics.Value is not { } statistics)
            return new(link, false, null, game.Profile.Message ?? game.Statistics.Message);
        if (!SteamHours.StillSameGame(link, profile.Name, profile.ExecutablePath, Installed(profile)?.AppId))
            return new(link, false, null, $"This game no longer matches “{link.SteamName}” on Steam, so Steam hours are not counted. Unlink it, then link it again.");
        if (await HolderAsync(link.AppId, id).ConfigureAwait(false) is { } other)
            return new(link, false, null, $"“{other}” is linked to the same Steam game. Unlink one of them.");
        var origins = (await store.LoadSessionOriginsAsync(id).ConfigureAwait(false)).Value;
        return new(link, true, SteamHours.Totals(link, statistics, origins), null);
    }

    /// <summary>Whether it is worth showing the Steam section at all. Local files only.</summary>
    public async Task<bool> CanBeLinkedAsync(Guid id)
    {
        var game = await store.LoadProfileAsync(id).ConfigureAwait(false);
        if (game.Profile.Value is not { IsArchived: false } profile) return false;
        return Installed(profile) is not null || (await store.LoadMetadataAsync(id).ConfigureAwait(false)).Value?.SteamAppId is not null;
    }

    /// <summary>The one Steam game this profile can be linked to, or why there is none. A copy outside Steam asks the Steam store about the looked-up id.</summary>
    public async Task<SteamOfferResult> FindOfferAsync(Guid id, CancellationToken cancellationToken = default)
    {
        static SteamOfferResult No(string reason) => new(null, reason);
        var game = await store.LoadProfileAsync(id).ConfigureAwait(false);
        if (game.Profile.State != DocumentState.Ready || game.Profile.Value is not { IsArchived: false } profile) return No("This game cannot be changed right now.");
        string appId, name;
        SteamAppSummary? details = null;
        var installed = Installed(profile);
        if (installed is { } proof)
        {
            appId = proof.AppId;
            name = SteamLocalData.InstalledName(proof.Root, appId) ?? profile.Name;
        }
        else
        {
            var looked = (await store.LoadMetadataAsync(id).ConfigureAwait(false)).Value?.SteamAppId;
            if (looked is null || !SteamLink.IsDigits(looked)) return No("No matching Steam game was found. Try Refresh info first.");
            try { details = await summary(looked, cancellationToken).ConfigureAwait(false); }
            catch (IOException error) { return No(error.Message); }
            if (details is null) return No("Steam could not be asked about this game. Try again later.");
            if (!SteamHours.TitlesMatch(profile.Name, details.Name)) return No($"The names differ (Steam: “{details.Name}”), so they cannot be linked. Rename the game if it is the same one.");
            appId = looked;
            name = details.Name;
        }
        if (await HolderAsync(appId, id).ConfigureAwait(false) is { } other) return No($"This Steam game is already linked to “{other}”.");
        var accounts = steam.Accounts();
        if (accounts.Count == 0) return No("No Steam account was found on this PC.");
        var played = accounts.Select(a => (Account: a, Minutes: steam.Minutes(a.Id, appId))).ToArray();
        var offered = played.Any(p => p.Minutes is not null) ? played.Where(p => p.Minutes is not null) : played;
        return new(new(appId, name.Length > 200 ? name[..200] : name, installed is not null, details,
            [.. offered.Select(p => new SteamAccountPlaytime(p.Account, p.Minutes ?? 0))]), null);
    }

    /// <summary>Links after checking every guard again, so an old offer cannot link the wrong game.</summary>
    public async Task LinkAsync(Guid id, SteamOffer offer, string accountId, CancellationToken cancellationToken = default)
    {
        var (current, reason) = await FindOfferAsync(id, cancellationToken).ConfigureAwait(false);
        if (current is null) throw new IOException(reason);
        var account = current.AppId == offer.AppId ? current.Accounts.FirstOrDefault(a => a.Account.Id == accountId) : null;
        if (account is null) throw new IOException("The Steam information changed. Look for the game again.");
        var profile = (await store.LoadProfileAsync(id).ConfigureAwait(false)).Profile.Value!;
        await store.SaveSteamLinkAsync(new(1, id, current.AppId, accountId, current.SteamName, !current.Proven, profile.ExecutablePath, account.Minutes, DateTimeOffset.UtcNow))
            .ConfigureAwait(false);
    }

    public Task UnlinkAsync(Guid id) => store.RemoveSteamLinkAsync(id);

    /// <summary>Reads Steam's current figures for every linked library game (at start-up). A figure Steam no longer has is kept as it was.</summary>
    /// <returns>How many games changed.</returns>
    public async Task<int> RefreshAsync()
    {
        var changed = 0;
        foreach (var game in (await store.InitializeAsync().ConfigureAwait(false)).Games.Where(g => g.Profile.Value is { IsArchived: false }))
            try
            {
                var stored = await store.LoadSteamLinkAsync(game.Id).ConfigureAwait(false);
                if (stored.IsReadOnly || stored.Value is not { } link) continue;
                if (steam.Minutes(link.AccountId, link.AppId) is not long minutes || minutes == link.Minutes) continue;
                await store.SaveSteamLinkAsync(link with { Minutes = minutes, ReadAt = DateTimeOffset.UtcNow }).ConfigureAwait(false);
                changed++;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        return changed;
    }

    private (string AppId, string Root)? Installed(GameProfile profile)
    {
        // The program itself is passed as the target: only "which installation holds this program" is asked here.
        try
        {
            var plan = launcher.Resolve(profile.ExecutablePath, profile.ExecutablePath);
            return plan is { Provider: StoreProvider.Steam } && plan.StoreKey.StartsWith("Steam:", StringComparison.Ordinal) ? (plan.StoreKey[6..], plan.InstallRoot) : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException) { return null; }
    }

    /// <summary>The name of another library game already linked to this Steam app.</summary>
    private async Task<string?> HolderAsync(string appId, Guid except)
    {
        foreach (var game in (await store.InitializeAsync().ConfigureAwait(false)).Games)
            if (game.Id != except && game.Profile.Value is { IsArchived: false } profile &&
                (await store.LoadSteamLinkAsync(game.Id).ConfigureAwait(false)).Value?.AppId == appId) return profile.Name;
        return null;
    }
}
