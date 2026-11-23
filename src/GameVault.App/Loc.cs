using System.ComponentModel;
using System.Globalization;
using System.Resources;
using System.Windows;
using System.Windows.Media;
using GameVault.Core;

namespace GameVault.App;

/// <summary>
/// Looks strings up in a resource set for one culture: the culture's own text, then the neutral English text, then the key itself
/// (a missing key is visible in the UI but never throws). Also knows the layout direction of that culture.
/// </summary>
public sealed class Localizer(ResourceManager manager) : INotifyPropertyChanged
{
    private CultureInfo culture = CultureInfo.GetCultureInfo("en");

    public event PropertyChangedEventHandler? PropertyChanged;

    public CultureInfo Culture
    {
        get => culture;
        set
        {
            if (Equals(culture, value)) return;
            culture = value;
            // "Item[]" refreshes every {Binding [key]} on the LocSource indexer.
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Culture)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        }
    }

    public FlowDirection FlowDirection => culture.TextInfo.IsRightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

    public string this[string key] => manager.GetString(key, culture) ?? manager.GetString(key, CultureInfo.InvariantCulture) ?? key;

    /// <summary>The text for <paramref name="key"/>, with <c>{0}</c>, <c>{1}</c>… replaced by <paramref name="args"/> formatted for the culture.</summary>
    public string T(string key, params object?[] args) =>
        args.Length == 0 ? this[key] : string.Format(culture, this[key], args);

    /// <summary>Sets the layout direction of a window or view; drawers and inline views inside it inherit it.</summary>
    public void ApplyFlowDirection(FrameworkElement element) => element.FlowDirection = FlowDirection;

    /// <summary>
    /// Flips a directional icon (back/forward chevrons, arrows) in a right-to-left layout. WPF mirrors layout, not drawn shapes.
    /// </summary>
    public void MirrorIcon(FrameworkElement icon)
    {
        icon.RenderTransformOrigin = new Point(0.5, 0.5);
        icon.RenderTransform = FlowDirection == FlowDirection.RightToLeft ? new ScaleTransform(-1, 1) : Transform.Identity;
    }
}

/// <summary>The app's single set of strings. New user-visible text goes into Strings.resx (English) and Strings.ar.resx, then through here or <c>{loc:Str Key}</c>.</summary>
public static class Loc
{
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en");
    private static readonly CultureInfo Arabic = CultureInfo.GetCultureInfo("ar");

    public static Localizer Current { get; } = new(new ResourceManager("GameVault.App.Strings", typeof(Loc).Assembly));

    /// <summary>The Windows display language, read once: it is what "System" follows even after the thread culture has been changed.</summary>
    public static CultureInfo SystemCulture { get; } = CultureInfo.InstalledUICulture;

    /// <summary>The language code in use ("en" or "ar").</summary>
    public static string Code { get; private set; } = LanguageChoice.English;

    public static event EventHandler? LanguageChanged;

    /// <summary>Applies a saved language choice: the UI culture of the process and the strings the app looks up. Unknown values mean English.</summary>
    public static void SetLanguage(string? choice)
    {
        Code = LanguageChoice.Resolve(choice, SystemCulture);
        var culture = LanguageChoice.IsRightToLeft(Code) ? Arabic : English;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentUICulture = culture;
        Current.Culture = culture;
        LanguageChanged?.Invoke(null, EventArgs.Empty);
    }

    public static string T(string key, params object?[] args) => Current.T(key, args);

    public static FlowDirection FlowDirection => Current.FlowDirection;

    /// <summary>The display name of a theme in the current language (the catalog's English name when no translation exists).</summary>
    public static string ThemeName(ThemeDefinition theme) => Lookup(ThemeKey(theme) + ".Name", theme.Name);

    public static string ThemeTagline(ThemeDefinition theme) => Lookup(ThemeKey(theme) + ".Tagline", theme.Tagline);

    /// <summary>The resource key stem for a theme: "Theme." plus its id without spaces and punctuation, for example Theme.ViceCity.</summary>
    public static string ThemeKey(ThemeDefinition theme) => "Theme." + new string(theme.Id.Where(char.IsLetterOrDigit).ToArray());

    private static string Lookup(string key, string fallback)
    {
        var value = Current[key];
        return value == key ? fallback : value;
    }
}

/// <summary>Binding source behind <c>{loc:Str Key}</c>: its indexer is re-read whenever the language changes.</summary>
public sealed class LocSource : INotifyPropertyChanged
{
    private readonly Localizer localizer;

    public LocSource(Localizer localizer)
    {
        this.localizer = localizer;
        // The sender must be this object for WPF's binding to refresh, so the Localizer's event is re-raised here.
        localizer.PropertyChanged += (_, e) => { if (e.PropertyName == "Item[]") PropertyChanged?.Invoke(this, e); };
    }

    public static LocSource Instance { get; } = new(Loc.Current);

    public event PropertyChangedEventHandler? PropertyChanged;

    public string this[string key] => localizer[key];
}
