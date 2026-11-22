using System.Globalization;

namespace GameVault.Core;

/// <summary>The facts GameVault can show for a game (ticket #83).</summary>
public enum MetadataField { Description, ReleaseDate, Developers, Publishers, Genres, InstallSize }

/// <summary>A date that may be only a year, a year and month, or complete. Stored as "2013", "2013-03" or "2013-03-21".</summary>
public sealed record PartialDate(int Year, int? Month = null, int? Day = null)
{
    public static bool IsValid(PartialDate? value) => value is { Year: >= 1970 and <= 2100 } &&
        (value.Month is null ? value.Day is null : value.Month is >= 1 and <= 12 && (value.Day is null || (value.Day >= 1 && value.Day <= DateTime.DaysInMonth(value.Year, value.Month.Value))));

    public override string ToString() => Month is null ? Year.ToString("0000", CultureInfo.InvariantCulture)
        : Day is null ? $"{Year:0000}-{Month:00}" : $"{Year:0000}-{Month:00}-{Day:00}";

    /// <summary>Text for people: "2013", "March 2013" or "21 March 2013".</summary>
    public string Display()
    {
        if (Month is null) return Year.ToString(CultureInfo.InvariantCulture);
        var month = CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(Month.Value);
        return Day is null ? $"{month} {Year}" : $"{Day} {month} {Year}";
    }

    /// <summary>Reads the storage form ("2013", "2013-03", "2013-03-21") or the loose forms stores use ("21 Mar, 2013", "Mar 21, 2013", "Mar 2013", "2013").</summary>
    public static PartialDate? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        text = text.Trim().Replace(",", " ").Replace(".", " ");
        text = string.Join(' ', text.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        var culture = CultureInfo.InvariantCulture;
        PartialDate? Check(PartialDate? date) => IsValid(date) ? date : null;
        if (text.Length == 4 && int.TryParse(text, NumberStyles.None, culture, out var y)) return Check(new(y));
        if (DateTime.TryParseExact(text, "yyyy-MM", culture, DateTimeStyles.None, out var ym) && text.Length == 7) return Check(new(ym.Year, ym.Month));
        if (DateTime.TryParseExact(text, "yyyy-MM-dd", culture, DateTimeStyles.None, out var ymd)) return Check(new(ymd.Year, ymd.Month, ymd.Day));
        if (DateTime.TryParseExact(text, ["d MMM yyyy", "d MMMM yyyy", "MMM d yyyy", "MMMM d yyyy"], culture, DateTimeStyles.None, out var full)) return Check(new(full.Year, full.Month, full.Day));
        if (DateTime.TryParseExact(text, ["MMM yyyy", "MMMM yyyy"], culture, DateTimeStyles.None, out var month)) return Check(new(month.Year, month.Month));
        return null;
    }
}

/// <summary>
/// Which game this is in the stores and databases, besides its Steam app id (kept as <see cref="GameMetadataDocument.SteamAppId"/>).
/// <see cref="Source"/> is "detected" (found from the install or by exact title) or "picked" (chosen by the owner, never changed automatically).
/// <see cref="NoMatch"/> means the owner said none of the candidates is right, so no title search is attempted.
/// </summary>
public sealed record GameIdentity(string? EpicSlug = null, string? EpicNamespace = null, string? GogId = null, string? WikidataId = null, string? HltbId = null, string Source = "detected", bool NoMatch = false)
{
    public static readonly GameIdentity Empty = new();
    public bool IsPicked => Source == "picked";

    /// <summary>Fills only the ids this identity lacks. A picked identity keeps every id it has.</summary>
    public GameIdentity FillFrom(GameIdentity? other) => other is null ? this : this with
    {
        EpicSlug = EpicSlug ?? other.EpicSlug, EpicNamespace = EpicNamespace ?? other.EpicNamespace, GogId = GogId ?? other.GogId,
        WikidataId = WikidataId ?? other.WikidataId, HltbId = HltbId ?? other.HltbId
    };

    private static bool Ok(string? id, int max = 80) => id is null || (id.Length is > 0 && id.Length <= max && id.All(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.'));
    public static bool IsValid(GameIdentity? value) => value is null || (Ok(value.EpicSlug, 120) && Ok(value.EpicNamespace, 64) && Ok(value.GogId, 16) && Ok(value.WikidataId, 16) && Ok(value.HltbId, 12) && value.Source is "detected" or "picked");
}

/// <summary>A game a store or database offers for a title, shown in the "Not the right game?" picker.</summary>
public sealed record MatchCandidate(string Source, string Id, string Title, string Detail, string? ImageUrl = null);

/// <summary>One stored fact and where it came from. A value the owner typed (<see cref="UserEdited"/>) is never replaced by a provider.</summary>
public sealed record MetadataValue(string Provider, bool UserEdited, DateTimeOffset At, string? Text = null, string[]? Items = null, long? Bytes = null, string? Date = null);

/// <summary>Per-game metadata (Profiles/&lt;game&gt;/metadata.json).</summary>
public sealed record GameMetadataDocument(int SchemaVersion, Guid GameId, Dictionary<string, MetadataValue> Fields, DateTimeOffset? LastSizeScan = null, DateTimeOffset? LastRefresh = null, string? SteamAppId = null, GameIdentity? Identity = null)
{
    public const int MaxTextLength = 4000;
    public const int MaxItems = 24;
    public const int MaxItemLength = 80;

    public static GameMetadataDocument Empty(Guid gameId) => new(1, gameId, []);

    public MetadataValue? Get(MetadataField field) => Fields.GetValueOrDefault(field.ToString());

    public static bool IsValid(GameMetadataDocument? value, Guid gameId) => value is { SchemaVersion: 1, Fields: not null } && value.GameId == gameId && (value.SteamAppId is null || (value.SteamAppId.Length is > 0 and <= 10 && value.SteamAppId.All(char.IsAsciiDigit))) && GameIdentity.IsValid(value.Identity) &&
        value.Fields.All(pair => Enum.TryParse<MetadataField>(pair.Key, out var field) && IsValidValue(field, pair.Value));

    private static bool IsValidValue(MetadataField field, MetadataValue? value)
    {
        if (value is null || string.IsNullOrWhiteSpace(value.Provider) || value.Provider.Length > 40) return false;
        return field switch
        {
            MetadataField.Description => value.Text is { Length: > 0 and <= MaxTextLength },
            MetadataField.ReleaseDate => PartialDate.Parse(value.Date) is { } d && d.ToString() == value.Date,
            MetadataField.InstallSize => value.Bytes is >= 0,
            _ => value.Items is { Length: > 0 and <= MaxItems } && value.Items.All(i => !string.IsNullOrWhiteSpace(i) && i.Length <= MaxItemLength)
        };
    }
}

/// <summary>Which providers may fill each field, in order, and the global switches (Data/metadata-settings.json).</summary>
/// <param name="AutoFetch">Look information up (and fetch a missing Steam cover) in the background for new games and for games that have none yet. On by default, like Playnite's download-on-import.</param>
public sealed record MetadataSettings(int SchemaVersion, bool OnlyFillEmpty, bool SizeOnDisk, string[] DisabledFields, Dictionary<string, string[]> ProviderOrder, bool AutoFetch = true)
{
    public static MetadataSettings Default => new(1, true, true, [], []);

    public bool IsEnabled(MetadataField field) => !DisabledFields.Contains(field.ToString());

    /// <summary>The configured provider order for a field, with any provider missing from it appended, so a newly added provider is never silently unused.</summary>
    public string[] OrderFor(MetadataField field, IEnumerable<string> capableProviders)
    {
        var capable = capableProviders.ToArray();
        var configured = ProviderOrder.GetValueOrDefault(field.ToString()) ?? [];
        return [.. configured.Where(id => capable.Contains(id, StringComparer.OrdinalIgnoreCase)), .. capable.Where(id => !configured.Contains(id, StringComparer.OrdinalIgnoreCase))];
    }

    public static bool IsValid(MetadataSettings? value) => value is { SchemaVersion: 1, DisabledFields: not null, ProviderOrder: not null } &&
        value.DisabledFields.All(f => Enum.TryParse<MetadataField>(f, out _)) &&
        value.ProviderOrder.All(p => Enum.TryParse<MetadataField>(p.Key, out _) && p.Value is { Length: <= 8 } order && order.All(id => !string.IsNullOrWhiteSpace(id) && id.Length <= 40));
}
