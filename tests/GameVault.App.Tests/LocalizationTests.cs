using System.Globalization;
using System.IO;
using System.Resources;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Xml.Linq;
using GameVault.Core;

namespace GameVault.App.Tests;

public sealed class LocalizationTests
{
    private static readonly CultureInfo Arabic = CultureInfo.GetCultureInfo("ar");
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en");

    private static Localizer Fixture(CultureInfo? culture = null) =>
        new(new ResourceManager("GameVault.App.Tests.LocFixture", typeof(LocalizationTests).Assembly)) { Culture = culture ?? English };

    private static void InSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception error) { failure = error; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw new Xunit.Sdk.XunitException(failure.ToString());
    }

    private static string AppSource(string file)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "GameVault.slnx"))) dir = dir.Parent;
        return Path.Combine(dir!.FullName, "src", "GameVault.App", file);
    }

    private static Dictionary<string, string> Keys(string file) =>
        XDocument.Load(AppSource(file)).Root!.Elements("data").ToDictionary(e => (string)e.Attribute("name")!, e => (string)e.Element("value")!);

    [Fact]
    public void EveryKeyExistsInBothResourceFiles()
    {
        var english = Keys("Strings.resx");
        var arabic = Keys("Strings.ar.resx");
        Assert.Empty(english.Keys.Except(arabic.Keys));
        Assert.Empty(arabic.Keys.Except(english.Keys));
        Assert.All(english, pair => Assert.False(string.IsNullOrWhiteSpace(pair.Value), pair.Key));
        Assert.All(arabic, pair => Assert.False(string.IsNullOrWhiteSpace(pair.Value), pair.Key));
    }

    [Fact]
    public void PlaceholdersMatchBetweenLanguages()
    {
        var english = Keys("Strings.resx");
        var arabic = Keys("Strings.ar.resx");
        static string[] Slots(string text) => Regex.Matches(text, @"\{\d+\}").Select(m => m.Value).Order().ToArray();
        Assert.All(english, pair => Assert.Equal(Slots(pair.Value), Slots(arabic[pair.Key])));
    }

    [Fact]
    public void EveryThemeHasANameAndATaglineInBothLanguages()
    {
        var english = Keys("Strings.resx");
        var arabic = Keys("Strings.ar.resx");
        foreach (var theme in ThemeCatalog.All)
        {
            var key = Loc.ThemeKey(theme);
            Assert.Equal(theme.Name, english[key + ".Name"]);
            Assert.Equal(theme.Tagline, english[key + ".Tagline"]);
            Assert.True(arabic.ContainsKey(key + ".Name") && arabic.ContainsKey(key + ".Tagline"), theme.Id);
            Assert.NotEqual(theme.Tagline, arabic[key + ".Tagline"]);
        }
    }

    [Fact]
    public void ArabicResourcesAreShippedWithTheAppAndResolve()
    {
        var localizer = new Localizer(new ResourceManager("GameVault.App.Strings", typeof(Loc).Assembly)) { Culture = Arabic };
        Assert.Equal(Keys("Strings.ar.resx")["Settings.Appearance"], localizer["Settings.Appearance"]);
        localizer.Culture = English;
        Assert.Equal("Appearance", localizer["Settings.Appearance"]);
    }

    [Fact]
    public void AMissingTranslationFallsBackToEnglishAndAnUnknownKeyReturnsTheKey()
    {
        var localizer = Fixture(Arabic);
        Assert.Equal("عربي", localizer["Both"]);
        Assert.Equal("English only", localizer["OnlyEnglish"]);
        Assert.Equal("No.Such.Key", localizer["No.Such.Key"]);
    }

    [Fact]
    public void FormattingFillsArguments()
    {
        Assert.Equal("Hello Sam", Fixture().T("WithArg", "Sam"));
        Assert.Equal("مرحبا Sam", Fixture(Arabic).T("WithArg", "Sam"));
    }

    [Fact]
    public void ThemeNamesFallBackToTheCatalogWhenThereIsNoTranslation()
    {
        var unlisted = ThemeCatalog.All[0] with { Id = "Not In Resources", Name = "Plain", Tagline = "Plain tagline" };
        Assert.Equal("Plain", Loc.ThemeName(unlisted));
        Assert.Equal("Plain tagline", Loc.ThemeTagline(unlisted));
    }

    [Fact]
    public void LanguageChoiceResolvesSystemAndKnownCodes()
    {
        Assert.Equal("ar", LanguageChoice.Resolve("System", CultureInfo.GetCultureInfo("ar-SA")));
        Assert.Equal("en", LanguageChoice.Resolve("System", CultureInfo.GetCultureInfo("fr-FR")));
        Assert.Equal("en", LanguageChoice.Resolve("en", CultureInfo.GetCultureInfo("ar-SA")));
        Assert.Equal("ar", LanguageChoice.Resolve("ar", CultureInfo.GetCultureInfo("en-US")));
        Assert.Equal("en", LanguageChoice.Resolve("xx", CultureInfo.GetCultureInfo("ar-SA")));
        Assert.True(LanguageChoice.IsKnown("System") && LanguageChoice.IsKnown("en") && LanguageChoice.IsKnown("ar"));
        Assert.False(LanguageChoice.IsKnown("fr") || LanguageChoice.IsKnown(null));
    }

    [Fact]
    public void FlowDirectionFlipsForArabicAndReachesChildren()
    {
        InSta(() =>
        {
            var localizer = Fixture();
            var child = new Border();
            var root = new Grid { Children = { child } };
            localizer.ApplyFlowDirection(root);
            Assert.Equal(FlowDirection.LeftToRight, root.FlowDirection);

            localizer.Culture = Arabic;
            localizer.ApplyFlowDirection(root);
            Assert.Equal(FlowDirection.RightToLeft, root.FlowDirection);
            Assert.Equal(FlowDirection.RightToLeft, child.FlowDirection);

            localizer.Culture = English;
            localizer.ApplyFlowDirection(root);
            Assert.Equal(FlowDirection.LeftToRight, child.FlowDirection);
        });
    }

    [Fact]
    public void DirectionalIconsMirrorOnlyInRightToLeft()
    {
        InSta(() =>
        {
            var localizer = Fixture();
            var icon = new System.Windows.Shapes.Path();
            localizer.MirrorIcon(icon);
            Assert.Equal(Transform.Identity, icon.RenderTransform);
            localizer.Culture = Arabic;
            localizer.MirrorIcon(icon);
            Assert.Equal(-1, ((ScaleTransform)icon.RenderTransform).ScaleX);
            localizer.Culture = English;
            localizer.MirrorIcon(icon);
            Assert.Equal(Transform.Identity, icon.RenderTransform);
        });
    }

    [Fact]
    public void BoundTextFollowsALiveLanguageChange()
    {
        InSta(() =>
        {
            var localizer = Fixture();
            var text = new TextBlock();
            text.SetBinding(TextBlock.TextProperty, new Binding("[Both]") { Source = new LocSource(localizer), Mode = BindingMode.OneWay });
            Assert.Equal("English both", text.Text);
            localizer.Culture = Arabic;
            Assert.Equal("عربي", text.Text);
        });
    }

    [Fact]
    public void KeysWithDotsWorkInTheXamlBindingPath()
    {
        InSta(() =>
        {
            var text = new TextBlock();
            text.SetBinding(TextBlock.TextProperty, new Binding("[Settings.Language.System]") { Source = LocSource.Instance, Mode = BindingMode.OneWay });
            Assert.Equal("System default", text.Text);
        });
    }

    [Fact]
    public void StrExtensionIsDeclaredAndTheSettingsAppearanceSectionUsesIt()
    {
        var xaml = File.ReadAllText(AppSource("MainWindow.xaml"));
        Assert.Contains("xmlns:loc=\"clr-namespace:GameVault.App\"", xaml);
        foreach (var key in new[] { "Settings.Appearance", "Settings.ColorTheme", "Settings.BrowseThemes", "Settings.Language" })
            Assert.Contains("{loc:Str " + key + "}", xaml);
    }
}
