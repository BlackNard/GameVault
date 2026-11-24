using System.Text.RegularExpressions;

namespace GameVault.Core;

/// <summary>
/// A version such as <c>0.1.0</c>, <c>0.1.0.3</c> or <c>0.1.0-preview.2</c> (an optional leading "v" and "+build" part are accepted and ignored).
/// Ordering follows semantic versioning: the numbers first, a release after its own previews, and preview identifiers compared one by one
/// (numbers numerically, words alphabetically, so preview.10 is newer than preview.9).
/// </summary>
public sealed record ReleaseVersion(int Major, int Minor, int Patch, int Revision, string? PreRelease) : IComparable<ReleaseVersion>
{
    private static readonly Regex Pattern = new(
        @"^[vV]?(?<a>\d{1,6})\.(?<b>\d{1,6})\.(?<c>\d{1,6})(?:\.(?<d>\d{1,6}))?(?:-(?<pre>[0-9A-Za-z]+(?:[.-][0-9A-Za-z]+)*))?(?:\+[0-9A-Za-z.-]+)?$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static bool TryParse(string? text, out ReleaseVersion version)
    {
        version = new(0, 0, 0, 0, null);
        if (string.IsNullOrWhiteSpace(text) || text.Length > 64) return false;
        var match = Pattern.Match(text.Trim());
        if (!match.Success) return false;
        version = new(int.Parse(match.Groups["a"].Value), int.Parse(match.Groups["b"].Value), int.Parse(match.Groups["c"].Value),
            match.Groups["d"].Success ? int.Parse(match.Groups["d"].Value) : 0, match.Groups["pre"].Success ? match.Groups["pre"].Value : null);
        return true;
    }

    public bool IsPreRelease => PreRelease is not null;

    public int CompareTo(ReleaseVersion? other)
    {
        if (other is null) return 1;
        foreach (var pair in new[] { (Major, other.Major), (Minor, other.Minor), (Patch, other.Patch), (Revision, other.Revision) })
            if (pair.Item1 != pair.Item2) return pair.Item1.CompareTo(pair.Item2);
        if (PreRelease is null && other.PreRelease is null) return 0;
        if (PreRelease is null) return 1;                    // 0.1.0 is newer than 0.1.0-preview.9
        if (other.PreRelease is null) return -1;
        return ComparePreRelease(PreRelease, other.PreRelease);
    }

    public static bool operator >(ReleaseVersion left, ReleaseVersion right) => left.CompareTo(right) > 0;
    public static bool operator <(ReleaseVersion left, ReleaseVersion right) => left.CompareTo(right) < 0;
    public static bool operator >=(ReleaseVersion left, ReleaseVersion right) => left.CompareTo(right) >= 0;
    public static bool operator <=(ReleaseVersion left, ReleaseVersion right) => left.CompareTo(right) <= 0;

    private static int ComparePreRelease(string left, string right)
    {
        var a = left.Split('.', '-');
        var b = right.Split('.', '-');
        for (var i = 0; i < Math.Min(a.Length, b.Length); i++)
        {
            var leftNumber = a[i].All(char.IsAsciiDigit);
            var rightNumber = b[i].All(char.IsAsciiDigit);
            int result;
            if (leftNumber && rightNumber) result = long.Parse(a[i]).CompareTo(long.Parse(b[i]));
            else if (leftNumber) result = -1;                 // numbers sort before words
            else if (rightNumber) result = 1;
            else result = string.CompareOrdinal(a[i], b[i]);
            if (result != 0) return Math.Sign(result);
        }
        return a.Length.CompareTo(b.Length);
    }

    public override string ToString() =>
        $"{Major}.{Minor}.{Patch}" + (Revision != 0 ? "." + Revision : "") + (PreRelease is null ? "" : "-" + PreRelease);
}
