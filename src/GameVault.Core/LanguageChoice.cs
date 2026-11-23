using System.Globalization;

namespace GameVault.Core;

/// <summary>
/// The language setting stored in settings.json: "System" (follow Windows) or the code of a shipped translation.
/// Adding a language means adding its code here, a <c>Strings.&lt;code&gt;.resx</c> and its name in the picker.
/// </summary>
public static class LanguageChoice
{
    public const string System = "System";
    public const string English = "en";
    public const string Arabic = "ar";

    public static IReadOnlyList<string> All { get; } = [System, English, Arabic];

    public static bool IsKnown(string? choice) => choice is not null && All.Contains(choice);

    /// <summary>The language code the app uses for this choice. "System" follows the given Windows display language; anything not shipped is English.</summary>
    public static string Resolve(string? choice, CultureInfo systemUiCulture)
    {
        var code = choice is null or System ? systemUiCulture.TwoLetterISOLanguageName : choice;
        return code == Arabic ? Arabic : English;
    }

    public static bool IsRightToLeft(string code) => code == Arabic;
}
