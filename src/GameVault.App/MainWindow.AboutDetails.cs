using System.IO;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using GameVault.Core;

namespace GameVault.App;

// The richer parts of "About this game": tags, features, languages, the long text, requirements, links and screenshots (phase 2 of the game data work).
public partial class MainWindow
{
    private void RenderAboutDetails(GameMetadataDocument? document)
    {
        AboutDetails.Children.Clear();
        if (document is null) return;
        void Chips(string label, string[]? items)
        {
            if (items is not { Length: > 0 }) return;
            var caption = new TextBlock { Text = label, Margin = new Thickness(0, 12, 0, 6) };
            caption.SetResourceReference(TextBlock.ForegroundProperty, "AppMuted");
            var wrap = new WrapPanel();
            foreach (var item in items)
            {
                var chip = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(9, 3, 9, 3), Margin = new Thickness(0, 0, 6, 6), Child = new TextBlock { Text = item, FontSize = 12.5 } };
                chip.SetResourceReference(Border.BackgroundProperty, "AppRaised");
                wrap.Children.Add(chip);
            }
            AboutDetails.Children.Add(caption);
            AboutDetails.Children.Add(wrap);
        }
        RenderTimeToBeat(document);
        Chips("Store tags (what players tag this game on the store, not your own tags)", document.Get(MetadataField.Tags)?.Items);
        Chips("Features", document.Get(MetadataField.Features)?.Items);
        Chips("Languages", document.Get(MetadataField.Languages)?.Items);

        void Section(string header, string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            var body = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 4), MaxWidth = 780 };
            AboutDetails.Children.Add(new Expander { Header = header, Content = body, Margin = new Thickness(0, 10, 0, 0) });
        }
        Section("Read the full description", document.Get(MetadataField.About)?.Text);
        Section("System requirements", document.Get(MetadataField.Requirements)?.Text);

        if (document.Get(MetadataField.Links)?.Items is { Length: > 0 } links)
        {
            var wrap = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
            foreach (var entry in links)
            {
                if (GameMetadataDocument.ParseLink(entry) is not { } link) continue;
                var button = new Button { Content = link.Label + " ↗", Margin = new Thickness(0, 0, 8, 6), ToolTip = link.Url };
                System.Windows.Automation.AutomationProperties.SetName(button, "Open " + link.Label + " in your browser");
                button.Click += (_, _) => OpenLink(link.Url);
                wrap.Children.Add(button);
            }
            AboutDetails.Children.Add(wrap);
        }

        if (document.Get(MetadataField.Screenshots)?.Items is { Length: > 0 } shots)
        {
            var strip = new WrapPanel { Margin = new Thickness(0, 8, 0, 0), Visibility = Visibility.Collapsed };
            var note = new TextBlock { Text = "Official screenshots from the store page, not your own captures. Your own screenshots and recordings are in the Media tab.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0), MaxWidth = 700, HorizontalAlignment = HorizontalAlignment.Left };
            note.SetResourceReference(TextBlock.ForegroundProperty, "AppMuted");
            AboutDetails.Children.Add(note);
            var show = new Button { Content = $"Show store screenshots ({shots.Length})", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 0), ToolTip = "Official pictures from the store page. They load from the store when you click." };
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
            AboutDetails.Children.Add(show);
            AboutDetails.Children.Add(strip);
        }
    }

    private static string Duration(int seconds)
    {
        if (seconds < 3600) return $"{Math.Max(1, seconds / 60)} min";
        var hours = seconds / 3600.0;
        return hours >= 100 ? $"{hours:0} h" : $"{hours:0.#} h";
    }

    /// <summary>Average hours to finish the game, as people measured them on HowLongToBeat, next to your own playtime.</summary>
    private void RenderTimeToBeat(GameMetadataDocument document)
    {
        if (document.Get(MetadataField.TimeToBeat)?.Times is not { HasAny: true } times) return;
        var heading = new TextBlock { Text = "How long to beat", FontSize = 17, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 16, 0, 2) };
        var explain = new TextBlock { Text = "Average time other players needed (from HowLongToBeat), not your own playtime.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
        explain.SetResourceReference(TextBlock.ForegroundProperty, "AppMuted");
        AboutDetails.Children.Add(heading);
        AboutDetails.Children.Add(explain);
        var row = new WrapPanel();
        void Tile(string label, int? seconds)
        {
            if (seconds is not > 0) return;
            var panel = new StackPanel();
            var caption = new TextBlock { Text = label, FontSize = 12 };
            caption.SetResourceReference(TextBlock.ForegroundProperty, "AppMuted");
            panel.Children.Add(caption);
            panel.Children.Add(new TextBlock { Text = Duration(seconds.Value), FontSize = 22, Margin = new Thickness(0, 2, 0, 0) });
            var tile = new Border { CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Padding = new Thickness(14, 10, 14, 10), Margin = new Thickness(0, 0, 10, 10), MinWidth = 130, Child = panel };
            tile.SetResourceReference(Border.BorderBrushProperty, "AppLine");
            tile.SetResourceReference(Border.BackgroundProperty, "AppSurface");
            row.Children.Add(tile);
        }
        Tile("Main story", times.MainSeconds);
        Tile("Main + extras", times.PlusSeconds);
        Tile("Completionist", times.CompleteSeconds);
        Tile("All play styles", times.AllSeconds);
        AboutDetails.Children.Add(row);
        if (times.Url is { } url && GameMetadataDocument.SafeLink(url))
        {
            var open = new Button { Content = "Open on HowLongToBeat ↗", HorizontalAlignment = HorizontalAlignment.Left, ToolTip = url };
            open.Click += (_, _) => OpenLink(url);
            AboutDetails.Children.Add(open);
        }
    }

    private void OpenLink(string url)
    {
        if (!GameMetadataDocument.SafeLink(url)) return;
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException) { ShowToast("Could not open the link", error.Message, true); }
    }
}
