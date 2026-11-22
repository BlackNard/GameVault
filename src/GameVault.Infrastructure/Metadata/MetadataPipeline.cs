using GameVault.Core;

namespace GameVault.Infrastructure.Metadata;

/// <summary>What a provider is told about a game. Providers must send nothing but the store app id (or nothing at all) over the network.</summary>
public sealed record MetadataRequest(Guid GameId, string Name, string ExecutablePath, string? InstallRoot, string? SteamAppId, string? SteamAppsFolder, DateTimeOffset? LastSizeScan, bool SizeOnDisk, bool ForceSizeScan, GameIdentity? Identity = null);

/// <summary>Everything one provider found. Fields it cannot supply stay null or empty.</summary>
public sealed record ProviderData(string? Description = null, PartialDate? ReleaseDate = null, string[]? Developers = null, string[]? Publishers = null, string[]? Genres = null, long? InstallSizeBytes = null, string? SteamAppId = null, GameIdentity? Identity = null)
{
    public bool Has(MetadataField field) => field switch
    {
        MetadataField.Description => !string.IsNullOrWhiteSpace(Description),
        MetadataField.ReleaseDate => ReleaseDate is not null && PartialDate.IsValid(ReleaseDate),
        MetadataField.Developers => Developers is { Length: > 0 },
        MetadataField.Publishers => Publishers is { Length: > 0 },
        MetadataField.Genres => Genres is { Length: > 0 },
        MetadataField.InstallSize => InstallSizeBytes is >= 0,
        _ => false
    };
}

public interface IGameMetadataProvider
{
    string Id { get; }
    string DisplayName { get; }
    IReadOnlySet<MetadataField> AvailableFields { get; }
    /// <summary>Returns null when it has nothing for this game. Throwing is allowed: the resolver treats it as "skip this provider".</summary>
    Task<ProviderData?> GetAsync(MetadataRequest request, CancellationToken cancellationToken);
}

/// <summary>A provider that can list games for a title, so the owner can pick the right one.</summary>
public interface IMatchSearchProvider : IGameMetadataProvider
{
    Task<IReadOnlyList<MatchCandidate>> SearchAsync(string title, CancellationToken cancellationToken);
}

public sealed record MetadataResolution(GameMetadataDocument Document, int FieldsFilled, string[] Problems, bool UsedNetwork);

/// <summary>
/// Chooses a value for each field: walk the providers in the configured order and take the first one with valid data. A provider that is missing,
/// does not support the field, returns nothing or fails is skipped (never fatal). Each provider is asked at most once per game, and only when a field
/// still needs it. A value the owner typed, or any existing value when "only fill empty" is on, is kept.
/// </summary>
public static class MetadataResolver
{
    public static async Task<MetadataResolution> ResolveAsync(MetadataRequest request, IReadOnlyList<IGameMetadataProvider> providers, MetadataSettings settings,
        GameMetadataDocument existing, CancellationToken cancellationToken, Func<IGameMetadataProvider, bool>? isNetworkProvider = null)
    {
        var fields = new Dictionary<string, MetadataValue>(existing.Fields);
        var responses = new Dictionary<string, ProviderData?>(StringComparer.OrdinalIgnoreCase);
        var problems = new List<string>();
        var filled = 0;
        var usedNetwork = false;
        var sizeScanned = existing.LastSizeScan;
        var steamId = request.SteamAppId;
        var identity = request.Identity ?? existing.Identity ?? GameIdentity.Empty;
        foreach (var field in Enum.GetValues<MetadataField>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!settings.IsEnabled(field)) continue;
            var current = existing.Get(field);
            if (current is not null && (current.UserEdited || settings.OnlyFillEmpty)) continue;
            var capable = providers.Where(p => p.AvailableFields.Contains(field)).ToArray();
            foreach (var id in settings.OrderFor(field, capable.Select(p => p.Id)))
            {
                var provider = capable.First(p => p.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
                if (!responses.TryGetValue(provider.Id, out var data))
                {
                    try
                    {
                        data = await provider.GetAsync(request with { SteamAppId = steamId, Identity = identity }, cancellationToken).ConfigureAwait(false);
                        if (isNetworkProvider?.Invoke(provider) == true) usedNetwork = true;
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                    catch (Exception error)
                    {
                        data = null;
                        problems.Add($"{provider.DisplayName}: {error.Message}");
                    }
                    responses[provider.Id] = data;
                    // What this provider learned (for example a Wikidata item that names the GOG and Steam ids) helps the providers asked after it.
                    if (data?.SteamAppId is { } learnedSteam && steamId is null) steamId = learnedSteam;
                    if (data?.Identity is { } learned) identity = identity.FillFrom(learned);
                }
                if (data is null || !data.Has(field)) continue;
                var value = ToValue(field, provider.Id, data);
                if (value is null) continue;
                fields[field.ToString()] = value;
                if (field == MetadataField.InstallSize) sizeScanned = DateTimeOffset.UtcNow;
                filled++;
                break;
            }
        }
        // Remember which Steam game this is, so covers and later lookups do not search again.
        var resolvedId = steamId ?? existing.SteamAppId;
        var storedIdentity = identity == GameIdentity.Empty ? existing.Identity : identity;
        var document = existing with { Fields = fields, LastSizeScan = sizeScanned, LastRefresh = DateTimeOffset.UtcNow, SteamAppId = resolvedId, Identity = storedIdentity };
        return new(document, filled, [.. problems.Distinct()], usedNetwork);
    }

    private static MetadataValue? ToValue(MetadataField field, string provider, ProviderData data)
    {
        var now = DateTimeOffset.UtcNow;
        MetadataValue? value = field switch
        {
            MetadataField.Description => new(provider, false, now, Text: Clip(data.Description!.Trim(), GameMetadataDocument.MaxTextLength)),
            MetadataField.ReleaseDate => new(provider, false, now, Date: data.ReleaseDate!.ToString()),
            MetadataField.Developers => new(provider, false, now, Items: Items(data.Developers!)),
            MetadataField.Publishers => new(provider, false, now, Items: Items(data.Publishers!)),
            MetadataField.Genres => new(provider, false, now, Items: Items(data.Genres!)),
            MetadataField.InstallSize => new(provider, false, now, Bytes: data.InstallSizeBytes),
            _ => null
        };
        return value;
    }

    private static string Clip(string text, int max) => text.Length <= max ? text : text[..(max - 1)].TrimEnd() + "…";

    private static string[] Items(IEnumerable<string> raw) => [.. raw.Select(i => Clip(string.Join(' ', (i ?? "").Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries)), GameMetadataDocument.MaxItemLength))
        .Where(i => i.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Take(GameMetadataDocument.MaxItems)];
}
