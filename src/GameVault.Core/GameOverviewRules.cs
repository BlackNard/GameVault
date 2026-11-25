using System.Globalization;

namespace GameVault.Core;

/// <summary>What a game page shows at first, and the wording of its compact lines (issue #76).</summary>
public static class GameOverviewRules
{
    public const int ShortDescriptionLength = 220;

    /// <summary>True when a description does not fit the few lines shown at first, so "Read more" is offered.</summary>
    public static bool NeedsReadMore(string? description) =>
        description is { Length: > 0 } && (description.Length > ShortDescriptionLength || description.Count(c => c == '\n') >= 3);

    public static string Hours(int seconds)
    {
        if (seconds < 3600) return $"{Math.Max(1, seconds / 60)} min";
        var hours = seconds / 3600.0;
        return (hours >= 100 ? hours.ToString("0", CultureInfo.CurrentCulture) : hours.ToString("0.#", CultureInfo.CurrentCulture)) + " h";
    }

    /// <summary>The player's progress against the main story (or, without it, the all-styles figure): a share from 0 to 1 and a sentence. Null without playtime or a figure.</summary>
    public static (double Share, string Text)? Progress(long? playedSeconds, int? main, int? all)
    {
        var target = main is > 0 ? main : all;
        if (playedSeconds is not > 0 || target is not > 0) return null;
        var share = playedSeconds.Value / (double)target.Value;
        var name = main is > 0 ? "the main story" : "an average playthrough";
        var percent = share >= 1 ? "past" : share < 0.01 ? "under 1% of" : Math.Floor(share * 100).ToString("0", CultureInfo.CurrentCulture) + "% of";
        var played = Hours((int)Math.Min(playedSeconds.Value, int.MaxValue));
        return (Math.Min(1, share), $"You have played {played}, {percent} {name}.");
    }
}
