using System.Text.RegularExpressions;
using GameVault.Core;
using GameVault.Infrastructure.Updates;

namespace GameVault.Tests;

/// <summary>"What's new" after an update (ticket #100): which notes a player is shown.</summary>
public sealed class ReleaseNotesTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "GameVault notes", Guid.NewGuid().ToString("N"));

    public void Dispose() { try { Directory.Delete(root, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }

    private static readonly ReleaseNote[] Notes = [.. new[] { 7, 10, 8, 9 }.Select(n => new ReleaseNote($"0.1.0-preview.{n}", ["new " + n], []))];

    private static string[] Shown(string? seen, int running, bool usedBefore) =>
        [.. ReleaseNotes.Unseen(seen, $"0.1.0-preview.{running}", usedBefore, Notes).Select(n => n.New[0])];

    [Fact]
    public void AFirstInstallIsToldNothing() => Assert.Empty(Shown(null, 8, usedBefore: false));

    [Fact]
    public void APlayerFromBeforeTheRecordIsToldAboutTheRunningVersionOnly() => Assert.Equal(["new 9"], Shown(null, 9, usedBefore: true));

    [Fact]
    public void EveryVersionInBetweenIsShownNewestFirst()
    {
        Assert.Equal(["new 8"], Shown("0.1.0-preview.7", 8, true));
        Assert.Equal(["new 10", "new 9", "new 8"], Shown("0.1.0-preview.7", 10, true));
    }

    [Fact]
    public void TheSameOrAnOlderVersionShowsNothingAndTheNewestSeenIsRemembered()
    {
        Assert.Empty(Shown("0.1.0-preview.9", 9, true));
        Assert.Empty(Shown("0.1.0-preview.9", 8, true));
        Assert.Equal("0.1.0-preview.9", ReleaseNotes.Newest("0.1.0-preview.9", "0.1.0-preview.8"));
        Assert.Equal("0.1.0-preview.9", ReleaseNotes.Newest("0.1.0-preview.8", "0.1.0-preview.9"));
        Assert.Equal("0.1.0-preview.9", ReleaseNotes.Newest("garbage", "0.1.0-preview.9"));
    }

    [Fact]
    public void TheSeenVersionIsKeptInItsOwnFile()
    {
        Directory.CreateDirectory(root);
        Assert.Null(SeenVersion.Read(root));
        SeenVersion.Write(root, "0.1.0-preview.8");
        Assert.Equal("0.1.0-preview.8", SeenVersion.Read(root));
    }

    /// <summary>A release cannot be built without its notes.</summary>
    [Fact]
    public void TheVersionBeingBuiltHasNotes()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "GameVault.slnx"))) dir = dir.Parent;
        var props = File.ReadAllText(Path.Combine(dir!.FullName, "Directory.Build.props"));
        var current = $"{Regex.Match(props, "<GameVaultVersion>([^<]+)<").Groups[1].Value}-preview.{Regex.Match(props, @"<GameVaultPreview>(\d+)<").Groups[1].Value}";
        var note = Assert.Single(ReleaseNotes.All, n => n.Version == current);
        Assert.NotEmpty(note.New.Concat(note.Fixed));
        Assert.All(ReleaseNotes.All, n => Assert.True(ReleaseVersion.TryParse(n.Version, out _)));
    }
}
