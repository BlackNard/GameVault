using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Xml.Linq;
using GameVault.Core;

namespace GameVault.App.Tests;

/// <summary>The shared motion helper and where the main window uses it (issue #55).</summary>
public sealed class MotionTests
{
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static XDocument Window([CallerFilePath] string here = "") =>
        XDocument.Load(Path.Combine(Path.GetDirectoryName(here)!, "..", "..", "src", "GameVault.App", "MainWindow.xaml"));

    [Theory]
    [InlineData("LibraryPanel", "Page")]
    [InlineData("SettingsPanel", "Page")]
    [InlineData("GamePage", "Page")]
    [InlineData("OverviewPanel", "Page")]
    [InlineData("StatisticsPanel", "Page")]
    [InlineData("NotesHost", "Page")]
    [InlineData("OverviewSection", "Section")]
    [InlineData("SavesSection", "Section")]
    [InlineData("MediaSection", "Section")]
    [InlineData("CatAppearance", "Section")]
    [InlineData("CatAdvanced", "Section")]
    [InlineData("DrawerLayer", "Fade")]
    [InlineData("DrawerPanel", "Drawer")]
    [InlineData("PaletteOverlay", "Dialog")]
    [InlineData("NoticePanel", "Dialog")]
    public void ScreensAndPanelsUseTheSharedMotion(string name, string kind)
    {
        var element = Window().Descendants().Single(e => (string?)e.Attribute(X + "Name") == name);
        Assert.Equal(kind, element.Attributes().FirstOrDefault(a => a.Name.LocalName == "Motion.Appear")?.Value);
    }

    [Fact]
    public void ToastsEaseInAndReduceMotionHasASwitch()
    {
        var window = Window();
        var toasts = window.Descendants().Single(e => (string?)e.Attribute(X + "Name") == "ToastHost");
        Assert.Contains(toasts.Descendants(), e => e.Attributes().Any(a => a.Name.LocalName == "Motion.Appear" && a.Value == "Toast"));
        Assert.Contains(window.Descendants(), e => (string?)e.Attribute(X + "Name") == "ReduceMotionChoice");
    }

    [Fact]
    public void AnElementSlidesInOnlyWhenMotionIsOnAndEndsWhereItBelongs() => OnSta(() =>
    {
        var still = new Border();
        Motion.ReduceMotion = true;
        Motion.Play(still, MotionKind.Page);
        Assert.Equal(Transform.Identity, still.RenderTransform);
        Assert.False(still.HasAnimatedProperties);

        Motion.ReduceMotion = false;
        var moving = new Border { Opacity = 0.8 };
        Motion.Play(moving, MotionKind.Page);
        if (SystemParameters.ClientAreaAnimation) Assert.True(moving.HasAnimatedProperties);
        Assert.Equal(0.8, (double)moving.GetAnimationBaseValue(UIElement.OpacityProperty));

        // An element that already has its own transform keeps it.
        var turned = new Border { RenderTransform = new RotateTransform(10) };
        Motion.Play(turned, MotionKind.Page);
        Assert.IsType<RotateTransform>(turned.RenderTransform);
    });

    [Fact]
    public void CardsLiftGlowAndFadeTheirCoversThroughTheSharedMotion()
    {
        var window = Window();
        var card = window.Descendants().Single(e => (string?)e.Attribute(X + "Key") == "CardTemplate");
        Assert.Contains(card.Descendants(), e => e.Attributes().Any(a => a.Name.LocalName == "Motion.Lift"));
        Assert.Contains(card.Descendants(), e => (string?)e.Attribute("Tag") == "Glow");
        Assert.Contains(card.Descendants(), e => e.Attributes().Any(a => a.Name.LocalName == "Motion.Stagger"));
        Assert.Equal(2, window.Descendants().Where(e => e.Name.LocalName == "Image")
            .Count(e => e.Attributes().Any(a => a.Name.LocalName == "Motion.Source" && a.Value == "{Binding Cover}")));
    }

    [Fact]
    public void ACoverIsShownAtOnceWithMotionOffAndNothingIsAddedToACard() => OnSta(() =>
    {
        Motion.ReduceMotion = true;
        var image = new Image();
        var picture = System.Windows.Media.Imaging.BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[4], 4);
        Motion.SetSource(image, picture);
        Assert.Same(picture, image.Source);
        Assert.False(image.HasAnimatedProperties);
        var glow = new Border { Tag = "Glow", Opacity = 0 };
        var frame = new Grid();
        frame.Children.Add(glow);
        Motion.Hover(frame, true);
        Assert.Null(glow.Effect);
        Assert.Equal(Transform.Identity, frame.RenderTransform);

        Motion.ReduceMotion = false;
        if (SystemParameters.ClientAreaAnimation)
        {
            Motion.Hover(frame, true);
            Assert.NotNull(glow.Effect);
            Assert.IsType<TranslateTransform>(frame.RenderTransform);
        }
        Motion.SetSource(image, null);
        Assert.Null(image.Source);
    });

    private static void OnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception e) { failure = e; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)));
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
