using System.Globalization;

namespace GameVault.Core;

/// <summary>The player's own rating of a game, in half stars (issue #59): 1 to 10 means 0.5 to 5 stars, 0 means no rating.</summary>
public sealed record GameRating(int SchemaVersion, Guid GameId, int HalfStars);

public static class StarRating
{
    public const int Stars = 5;
    public const int Max = Stars * 2;

    public static bool IsValid(int halfStars) => halfStars is >= 0 and <= Max;

    /// <summary>What a click choosing <paramref name="chosen"/> does: choosing the current rating again clears it.</summary>
    public static int Toggle(int current, int chosen) => Math.Clamp(chosen, 0, Max) == current ? 0 : Math.Clamp(chosen, 0, Max);

    /// <summary>The half stars under a pointer at <paramref name="x"/> across a row of stars <paramref name="width"/> wide (the left half of a star is a half).</summary>
    public static int FromPosition(double x, double width) => width <= 0 ? 0 : Math.Clamp((int)Math.Ceiling(x / width * Max), 1, Max);

    /// <summary>How much of star number <paramref name="index"/> (0 to 4) is filled: 0, 0.5 or 1.</summary>
    public static double Fill(int halfStars, int index) => Math.Clamp(halfStars - index * 2, 0, 2) / 2.0;

    /// <summary>"3.5 of 5 stars", or "Not rated".</summary>
    public static string Describe(int halfStars) => halfStars <= 0 ? "Not rated" : (halfStars / 2.0).ToString("0.#", CultureInfo.InvariantCulture) + " of 5 stars";

    /// <summary>"★★★½☆" for small read-only places.</summary>
    public static string Text(int halfStars) => halfStars <= 0 ? "" :
        new string('★', halfStars / 2) + (halfStars % 2 == 1 ? "½" : "") + new string('☆', Stars - halfStars / 2 - halfStars % 2);
}

/// <summary>The library's "filter by rating" choices.</summary>
public static class RatingFilters
{
    public const string Any = "Any";
    public const string Rated = "Rated";
    public const string AtLeast3 = "3";
    public const string AtLeast4 = "4";
    public const string Five = "5";
    public const string Unrated = "Unrated";
    public static readonly string[] All = [Any, Rated, AtLeast3, AtLeast4, Five, Unrated];

    public static string Label(string filter) => filter switch
    { Rated => "Rated games", AtLeast3 => "3 stars or more", AtLeast4 => "4 stars or more", Five => "5 stars", Unrated => "Not rated", _ => "Any rating" };

    public static bool Matches(string filter, int halfStars) => filter switch
    { Rated => halfStars > 0, AtLeast3 => halfStars >= 6, AtLeast4 => halfStars >= 8, Five => halfStars >= StarRating.Max, Unrated => halfStars <= 0, _ => true };
}
