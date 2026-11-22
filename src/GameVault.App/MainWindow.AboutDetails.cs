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
        Chips("Popular tags", document.Get(MetadataField.Tags)?.Items);
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
            var show = new Button { Content = $"Show screenshots ({shots.Length})", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 12, 0, 0), ToolTip = "Loads the pictures from the store when you click" };
            show.Click += (_, _) =>
            {
                show.IsEnabled = false;
                show.Content = "Screenshots";
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

    private void OpenLink(string url)
    {
        if (!GameMetadataDocument.SafeLink(url)) return;
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException) { ShowToast("Could not open the link", error.Message, true); }
    }
}
