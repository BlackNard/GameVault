using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Xml.Linq;

namespace GameVault.App.Tests;

/// <summary>Settings show option names and controls; explanations wait for hover or keyboard focus (issue #77).</summary>
public sealed class SettingsTextTests
{
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static XElement SettingsPanel([CallerFilePath] string here = "") =>
        XDocument.Load(Path.Combine(Path.GetDirectoryName(here)!, "..", "..", "src", "GameVault.App", "MainWindow.xaml"))
            .Descendants().Single(e => (string?)e.Attribute(X + "Name") == "SettingsPanel");

    private static string? Help(XElement element) => element.Attributes().FirstOrDefault(a => a.Name.LocalName == "SettingHelp.Text")?.Value;

    [Fact]
    public void NoLongExplanationIsShownByDefault()
    {
        var shown = SettingsPanel().Descendants().Where(e => (string?)e.Attribute("Style") == "{StaticResource SettingText}")
            .Select(e => (string?)e.Attribute("Text") ?? "").ToArray();
        Assert.All(shown, text => Assert.True(text.Length <= 110, $"Move this to hover help: {text}"));
    }

    [Fact]
    public void EveryOptionRowExplainsItselfShortlyOnHover()
    {
        var rows = SettingsPanel().Descendants().Where(e => (string?)e.Attribute("Style") == "{StaticResource SettingRow}")
            .Where(r => r.Descendants().Any(e => e.Name.LocalName is "CheckBox" or "Button") && r.Descendants().All(e => (string?)e.Attribute(X + "Name") is not ("SavedViewsManager" or "MetadataFields")))
            .ToArray();
        Assert.True(rows.Length >= 9);
        Assert.All(rows, row =>
        {
            var help = Help(row);
            Assert.False(string.IsNullOrWhiteSpace(help), "A settings row has no hover help: " + row.Descendants().Select(e => (string?)e.Attribute("Text")).FirstOrDefault(t => t is not null));
            Assert.True(help!.Length <= 230, "Keep hover help to one or two short sentences: " + help);
        });
    }

    [Fact]
    public void WhatIsReplacedOrSentOverTheNetworkStaysVisible()
    {
        var visible = string.Join(" ", SettingsPanel().Descendants().Select(e => (string?)e.Attribute("Text")).Where(t => t is not null));
        foreach (var statement in new[] { "Restoring replaces the whole library", "nothing about you is sent", "titles of other games are sent", "Downloaded from GitHub only when you press", "Creative Commons" })
            Assert.Contains(statement, visible);
    }

    [Fact]
    public void LongListsStayClosedUntilOpened()
    {
        var panel = SettingsPanel();
        foreach (var name in new[] { "MetadataSources", "SavedViewsSection", "BackupGuide" })
        {
            var section = panel.Descendants().Single(e => (string?)e.Attribute(X + "Name") == name);
            Assert.Equal("Expander", section.Name.LocalName);
            Assert.Null(section.Attribute("IsExpanded"));
        }
    }

    [Fact]
    public void HoverHelpReachesTheRowItsControlsAndScreenReaders() => OnSta(() =>
    {
        var button = new Button { Content = "Do it" };
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = "An option" });
        panel.Children.Add(button);
        var row = new Border();
        row.BeginInit();
        SettingHelp.SetText(row, "What it does.");
        row.Child = panel;
        row.EndInit();
        Assert.Equal("What it does.", ((TextBlock)((ToolTip)row.ToolTip).Content).Text);
        Assert.Equal("What it does.", AutomationProperties.GetHelpText(button));
        Assert.NotNull(button.ToolTip);
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
