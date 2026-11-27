using System.Text.RegularExpressions;

namespace GameVault.Core;

/// <summary>
/// How a game's name as the player writes it is matched with the name a store uses (ticket #105): other ways of writing the name to ask for, and a
/// strict rule for which answer is the same game. A different number or an unrelated extra word is always a different game.
/// </summary>
public static class GameTitles
{
    /// <summary>The most requests one source is sent for one title.</summary>
    public const int MaxQueries = 6;

    /// <summary>Words that name a release of a game, not another game.</summary>
    private static readonly HashSet<string> EditionWords = new(StringComparer.Ordinal)
    {
        "remake", "remastered", "remaster", "definitive", "complete", "ultimate", "deluxe", "gold", "goty", "edition", "anniversary", "enhanced", "hd",
        "redux", "directors", "cut", "special", "collectors", "standard", "pc", "version", "intergrade", "premium", "legendary", "the"
    };

    private static readonly Dictionary<string, string> ShortNames = new(StringComparer.Ordinal)
    {
        ["gta"] = "grand theft auto", ["cod"] = "call of duty", ["nfs"] = "need for speed", ["rdr"] = "red dead redemption",
        ["mgs"] = "metal gear solid", ["tlou"] = "the last of us", ["gow"] = "god of war"
    };

    // II to XX without I and X alone, which are also ordinary words and letters.
    private static readonly string[] Romans = ["", "", "ii", "iii", "iv", "v", "vi", "vii", "viii", "ix", "", "xi", "xii", "xiii", "xiv", "xv", "xvi", "xvii", "xviii", "xix", "xx"];

    /// <summary>The words of a title in one spelling: lower case, no punctuation or trademark signs, numbers as digits, short names written out.</summary>
    public static string[] Words(string? title)
    {
        var text = (title ?? "").ToLowerInvariant().Replace("&", " and ").Replace("'", "").Replace("’", "");
        text = Regex.Replace(text, @"(?<=\p{L})(?=\d)|(?<=\d)(?=\p{L})", " ");
        var words = Regex.Matches(text, @"[\p{L}\p{Nd}]+").Select(m => m.Value).ToList();
        for (var i = 0; i < words.Count; i++)
        {
            var number = words[i].Length > 0 ? Array.IndexOf(Romans, words[i]) : -1;
            if (number > 1) words[i] = number.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        if (words.Count > 0 && ShortNames.TryGetValue(words[0], out var full)) { words.RemoveAt(0); words.InsertRange(0, full.Split(' ')); }
        return [.. words];
    }

    /// <summary>The words without the ones at the end that only name an edition ("Silent Hill 2 Remake" is "silent hill 2").</summary>
    private static string[] Core(string? title)
    {
        var words = Words(Regex.Replace(title ?? "", @"\((19|20)\d\d\)|game of the year", " ", RegexOptions.IgnoreCase)).ToList();
        while (words.Count > 1 && EditionWords.Contains(words[^1])) words.RemoveAt(words.Count - 1);
        return [.. words.Where(w => w != "the")];
    }

    /// <summary>
    /// How well a store's name fits the wanted one; lower is better, null means it is another game. 0: the same letters and digits. 1: the same words.
    /// 2: the same words in another order. 3: the store adds only edition words. 4: the wanted name has edition words the store's does not.
    /// 5: both differ only in their edition words.
    /// </summary>
    public static int? Fit(string? wanted, string? found)
    {
        static string Letters(string? t) => new((t ?? "").Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
        var letters = Letters(wanted);
        if (letters.Length == 0) return null;
        if (letters == Letters(found)) return 0;
        var want = Words(wanted).Where(w => w != "the").ToArray();
        var have = Words(found).Where(w => w != "the").ToArray();
        if (want.Length == 0 || have.Length == 0) return null;
        if (want.SequenceEqual(have)) return 1;
        if (Same(want, have)) return 2;
        if (want.Length >= 2 && Extra(have, want) is { } added && added.All(EditionWords.Contains)) return 3;
        var core = Core(wanted);
        if (core.Length == 0) return null;
        if (Same(core, have)) return 4;
        return Same(core, Core(found)) ? 5 : null;
    }

    private static bool Same(string[] first, string[] second) => first.Order(StringComparer.Ordinal).SequenceEqual(second.Order(StringComparer.Ordinal));

    /// <summary>The words of <paramref name="all"/> left after taking each of <paramref name="part"/> once; null when one of them is missing.</summary>
    private static List<string>? Extra(string[] all, string[] part)
    {
        var left = all.ToList();
        foreach (var word in part)
            if (!left.Remove(word)) return null;
        return left;
    }

    /// <summary>The one best fitting answer. Two different answers that fit equally well are not guessed: <c>Ambiguous</c> is set and nothing is returned.</summary>
    public static (T? Match, int Fit, bool Ambiguous) Best<T>(string wanted, IEnumerable<T> found, Func<T, string> name) where T : class
    {
        var fits = found.Select(item => (Item: item, Fit: Fit(wanted, name(item)))).Where(f => f.Fit is not null).ToArray();
        if (fits.Length == 0) return (null, int.MaxValue, false);
        var best = fits.Min(f => f.Fit!.Value);
        var top = fits.Where(f => f.Fit == best).ToArray();
        return top.Length == 1 ? (top[0].Item, best, false) : (null, best, true);
    }

    /// <summary>
    /// Other ways to ask for a title, for when it finds nothing as typed: without edition words, short names written out, numbers in the other style,
    /// and finally fewer words. The answer must still pass <see cref="Fit"/> against what was typed.
    /// </summary>
    public static IReadOnlyList<string> Fallbacks(string? title)
    {
        var typed = Regex.Replace((title ?? "").Trim(), @"\s+", " ");
        var asked = new List<string>();
        void Add(IEnumerable<string> words)
        {
            var term = string.Join(' ', words).Trim();
            if (term.Length >= 2 && !term.Equals(typed, StringComparison.OrdinalIgnoreCase) && !asked.Contains(term, StringComparer.OrdinalIgnoreCase)) asked.Add(term);
        }
        var raw = Regex.Matches(Regex.Replace(typed, @"\((19|20)\d\d\)|game of the year", " ", RegexOptions.IgnoreCase), @"[\p{L}\p{Nd}'’&.]+").Select(m => m.Value).ToList();
        if (raw.Count == 0) return asked;
        var core = raw.ToList();
        while (core.Count > 1 && EditionWords.Contains(core[^1].ToLowerInvariant().Replace("'", ""))) core.RemoveAt(core.Count - 1);
        if (core.Count < raw.Count) Add(core);
        if (ShortNames.TryGetValue(core[0].ToLowerInvariant(), out var full)) { core = [.. System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(full).Split(' '), .. core.Skip(1)]; Add(core); }
        // "IV" and "4": a store may use either. Digits become Roman only in longer names, where it is a part number and not the name itself.
        var swapped = core.Select(word =>
        {
            var roman = Array.IndexOf(Romans, word.ToLowerInvariant());
            if (roman > 1) return roman.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return core.Count >= 3 && int.TryParse(word, out var number) && number > 1 && number < Romans.Length && Romans[number].Length > 0 ? Romans[number].ToUpperInvariant() : word;
        }).ToList();
        Add(swapped);
        for (var count = core.Count - 1; core.Count >= 4 && count >= Math.Max(2, core.Count - 2); count--) Add(core.Take(count));
        return asked;
    }
}
