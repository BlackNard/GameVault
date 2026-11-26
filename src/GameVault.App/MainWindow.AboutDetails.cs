using System.IO;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using GameVault.Core;

namespace GameVault.App;

// The rest of "About this game", shown on demand (issue #76): more facts, tags, features and languages, the long text, requirements, links and screenshots.
public partial class MainWindow
{
    /// <summary>Sections the player opened, kept open on other game pages while GameVault runs.</summary>
    private readonly HashSet<string> openSections = new(StringComparer.Ordinal);

    private void RememberSection(Expander section, string key)
    {
        section.IsExpanded = openSections.Contains(key);
        section.Expanded += (_, e) => { if (e.OriginalSource == section) openSections.Add(key); };
        section.Collapsed += (_, e) => { if (e.OriginalSource == section) openSections.Remove(key); };
    }

    private static TextBlock Muted(string text, Thickness margin = default)
    {
        var block = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = margin };
        block.SetResourceReference(TextBlock.ForegroundProperty, "AppMuted");
        return block;
    }

    private static StackPanel FactPanel(string label, string value)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 32, 8), MaxWidth = 420 };
        panel.Children.Add(Muted(label));
        panel.Children.Add(new TextBlock { Text = value, Margin = new Thickness(0, 2, 0, 0), TextWrapping = TextWrapping.Wrap });
        return panel;
    }

    private static Border Chip(string text)
    {
        var chip = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(9, 3, 9, 3), Margin = new Thickness(0, 0, 6, 6), Child = new TextBlock { Text = text, FontSize = 12.5 } };
        chip.SetResourceReference(Border.BackgroundProperty, "AppRaised");
        return chip;
    }

    private void RenderAboutDetails(GameMetadataDocument? document, IReadOnlyList<(string Label, string Value)> moreFacts)
    {
        AboutDetails.Children.Clear();

        void Section(string key, string header, FrameworkElement content)
        {
            content.Margin = new Thickness(0, 10, 0, 4);
            var section = new Expander { Header = header, Content = content };
            section.SetResourceReference(StyleProperty, "QuietExpander");
            RememberSection(section, key);
            AboutDetails.Children.Add(section);
        }

        if (moreFacts.Count > 0)
        {
            var facts = new WrapPanel();
            foreach (var (label, value) in moreFacts) facts.Children.Add(FactPanel(label, value));
            Section("facts", "More details", facts);
        }
        if (document is null) return;

        var chips = new StackPanel();
        void Chips(string label, string[]? items)
        {
            if (items is not { Length: > 0 }) return;
            chips.Children.Add(Muted(label, new Thickness(0, chips.Children.Count == 0 ? 0 : 8, 0, 6)));
            var wrap = new WrapPanel();
            foreach (var item in items) wrap.Children.Add(Chip(item));
            chips.Children.Add(wrap);
        }
        Chips("Store tags", document.Get(MetadataField.Tags)?.Items);
        Chips("Features", document.Get(MetadataField.Features)?.Items);
        Chips("Languages", document.Get(MetadataField.Languages)?.Items);
        if (chips.Children.Count > 0) Section("tags", "Store tags, features and languages", chips);

        void Text(string key, string header, string? text)
        {
            if (!string.IsNullOrWhiteSpace(text)) Section(key, header, new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap });
        }
        Text("about", "Full description", document.Get(MetadataField.About)?.Text);
        Text("requirements", "System requirements", document.Get(MetadataField.Requirements)?.Text);

        var extras = new StackPanel();
        var linkRow = new WrapPanel();
        foreach (var entry in document.Get(MetadataField.Links)?.Items ?? [])
        {
            if (GameMetadataDocument.ParseLink(entry) is not { } link) continue;
            AddLink(linkRow, link.Label, link.Url);
        }
        if (linkRow.Children.Count > 0) extras.Children.Add(linkRow);
        var shots = document.Get(MetadataField.Screenshots)?.Items;
        if (shots is { Length: > 0 }) AddScreenshots(extras, shots);
        if (extras.Children.Count > 0) Section("links", shots is { Length: > 0 } ? "Links and store screenshots" : "Links", extras);
    }

    private void AddLink(Panel row, string label, string url)
    {
        var button = new Button { Content = label + " ↗", Margin = new Thickness(0, 0, 8, 6), ToolTip = url };
        System.Windows.Automation.AutomationProperties.SetName(button, "Open " + label + " in your browser");
        button.Click += (_, _) => OpenLink(url);
        row.Children.Add(button);
    }

    private void AddScreenshots(Panel host, string[] shots)
    {
        var strip = new WrapPanel { Margin = new Thickness(0, 8, 0, 0), Visibility = Visibility.Collapsed };
        var show = new Button { Content = $"Show store screenshots ({shots.Length})", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 0), ToolTip = "Official pictures from the store page (not your own captures). They load from the store when you click." };
        show.Click += (_, _) =>
        {
            show.IsEnabled = false;
            show.Content = "Store screenshots";
            foreach (var url in shots.Take(8))
            {
                try
                {
                    // Only the picture addresses the store gave (checked against the image hosts); they load now because you asked.
                    if (!HeroImages.IsAllowedUrl(url)) continue;
                    var image = new BitmapImage();
                    image.BeginInit();
                    image.UriSource = new Uri(url);
                    image.DecodePixelWidth = 360;
                    image.CacheOption = BitmapCacheOption.OnLoad;
                    image.EndInit();
                    strip.Children.Add(new Border { CornerRadius = new CornerRadius(6), ClipToBounds = true, Margin = new Thickness(0, 0, 8, 8), Child = new Image { Source = image, Width = 360, Stretch = System.Windows.Media.Stretch.Uniform } });
                }
                catch (Exception error) when (error is UriFormatException or NotSupportedException or InvalidOperationException or IOException) { }
            }
            strip.Visibility = Visibility.Visible;
        };
        host.Children.Add(show);
        host.Children.Add(strip);
    }

    /// <summary>HowLongToBeat averages as cards, with your own progress under them.</summary>
    private void RenderTimeToBeat(GameMetadataDocument? document, long? playedSeconds)
    {
        var times = document?.Get(MetadataField.TimeToBeat)?.Times;
        shownTimes = (times, aboutFor, playedSeconds);
        TimeToBeatCards.Children.Clear();
        TimeToBeatBlock.Visibility = times is { HasAny: true } ? Visibility.Visible : Visibility.Collapsed;
        if (times is not { HasAny: true }) return;
        void Card(string label, int? seconds)
        {
            if (seconds is not > 0) return;
            var panel = new StackPanel();
            var caption = Muted(label);
            caption.FontSize = 12;
            panel.Children.Add(caption);
            panel.Children.Add(new TextBlock { Text = GameOverviewRules.Hours(seconds.Value), FontSize = 22, Margin = new Thickness(0, 2, 0, 0) });
            var card = new Border { CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Padding = new Thickness(14, 10, 14, 10), Margin = new Thickness(0, 0, 10, 10), MinWidth = 130, Child = panel };
            card.SetResourceReference(Border.BorderBrushProperty, "AppLine");
            card.SetResourceReference(Border.BackgroundProperty, "AppSurface");
            TimeToBeatCards.Children.Add(card);
        }
        Card("Main story", times.MainSeconds);
        Card("Main + extras", times.PlusSeconds);
        Card("Completionist", times.CompleteSeconds);
        Card("All play styles", times.AllSeconds);
        ShowTimeProgress();
        var link = times.Url is { } url && GameMetadataDocument.SafeLink(url) ? url : null;
        TimeToBeatLink.Tag = TimeToBeatLink.ToolTip = link;
        TimeToBeatLink.Visibility = link is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private (BeatTimes? Times, Guid Game, long? Played) shownTimes;

    /// <summary>Your progress against the averages; with an active Steam link it uses the combined total, like the card at the top.</summary>
    private void ShowTimeProgress()
    {
        if (shownTimes.Times is not { HasAny: true } times) return;
        var progress = GameOverviewRules.Progress(ShownPlayedSeconds(shownTimes.Game, shownTimes.Played), times.MainSeconds, times.AllSeconds);
        TimeProgressBar.Visibility = TimeProgressText.Visibility = progress is null ? Visibility.Collapsed : Visibility.Visible;
        if (progress is { } own) { TimeProgressBar.Value = own.Share; TimeProgressText.Text = own.Text; }
    }

    private void TimeToBeatLinkClicked(object sender, RoutedEventArgs e)
    {
        if (TimeToBeatLink.Tag is string url) OpenLink(url);
    }

    private void ReadMoreClicked(object sender, RoutedEventArgs e) => ShowWholeDescription(double.IsFinite(AboutText.MaxHeight));

    private void ShowWholeDescription(bool whole)
    {
        AboutText.MaxHeight = whole ? double.PositiveInfinity : 60;
        ReadMoreButton.Content = whole ? "Show less" : "Read more";
    }

    private void OpenLink(string url)
    {
        if (!GameMetadataDocument.SafeLink(url)) return;
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException) { ShowToast("Could not open the link", error.Message, true); }
    }
}
