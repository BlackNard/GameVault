using GameVault.Core;

namespace GameVault.Tests;

public sealed class GamePathSuggestionsTests
{
    [Fact]
    public void ExecutableSelectionFillsBlankWorkingDirectory() =>
        Assert.Equal(@"D:\Games\Resident Evil 2", GamePathSuggestions.WorkingDirectory("", "", @"D:\Games\Resident Evil 2\re2.exe"));

    [Fact]
    public void ExecutableSelectionUpdatesPreviousDefault() =>
        Assert.Equal(@"D:\Games\New", GamePathSuggestions.WorkingDirectory(@"C:\Games\Old\old.exe", @"c:\games\old", @"D:\Games\New\new.exe"));

    [Fact]
    public void ExecutableSelectionPreservesCustomWorkingDirectory() =>
        Assert.Equal(@"D:\Custom working folder", GamePathSuggestions.WorkingDirectory(@"C:\Games\old.exe", @"D:\Custom working folder", @"D:\Games\New\new.exe"));
    [Theory]
    [InlineData("", @"D:\Games\Resident Evil 2\re2.exe", "Resident Evil 2")]
    [InlineData("   ", @"D:\Games\Resident Evil 2\re2.exe", "Resident Evil 2")]
    [InlineData("My custom title", @"D:\Games\Resident Evil 2\re2.exe", "My custom title")]
    [InlineData("", "\"D:\\Games\\Resident Evil 2\\re2.exe\"", "Resident Evil 2")]
    [InlineData("", "D:/Games/Resident Evil 2/re2.exe", "Resident Evil 2")]
    [InlineData("", @"D:\re2.exe", "re2")]
    public void ExecutableSelectionSuggestsContainingFolderWithoutReplacingCustomName(string current, string executable, string expected) =>
        Assert.Equal(expected, GamePathSuggestions.GameName(current, executable));
}
