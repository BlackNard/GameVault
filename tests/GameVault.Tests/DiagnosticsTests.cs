using System.IO.Compression;
using GameVault.Core;
using GameVault.Infrastructure.Diagnostics;

namespace GameVault.Tests;

public sealed class DiagnosticsTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "gv-diag-" + Guid.NewGuid().ToString("N"));
    private readonly ApplicationPaths paths;

    public DiagnosticsTests()
    {
        Directory.CreateDirectory(Path.Combine(root, "Profiles", "Secret Game", "Notes", "Sessions"));
        File.WriteAllText(Path.Combine(root, "Profiles", "Secret Game", "Notes", "Sessions", "n.md"), "MY PRIVATE NOTE TEXT");
        paths = ApplicationPaths.FromRoot(root);
    }

    public void Dispose() => Directory.Delete(root, true);

    // Built from pieces so the source never contains something that looks like a real credential (the release audit scans history for that).
    private static readonly string FakeToken = "gh" + "p_" + "abcdefghijklmnopqrstuvwxyz0123456789ABCD";

    private DiagnosticsInput Input(bool names) => new("1.0", "Windows 11", ".NET 10", "GPU X", true, true,
        "{\"theme\":\"Midnight\",\"folder\":\"" + Environment.GetFolderPath(Environment.SpecialFolder.UserProfile).Replace("\\", "\\\\") + "\\\\Games\"}",
        1, 3, ["Secret Game"],
        [$"ERROR failed to open {Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Saved Games", "Secret Game")} token {FakeToken} for {Environment.UserName}"], names);

    [Fact]
    public void The_package_contains_no_user_name_user_folder_note_text_token_or_game_name()
    {
        var files = DiagnosticsPackage.Build(paths, Input(false));
        var all = string.Join("\n", files.Select(f => f.Name + "\n" + f.Text));
        var unmarked = System.Text.RegularExpressions.Regex.Replace(all, "<[a-z0-9 ]+>", "");   // placeholders such as <user profile> may contain the user name by chance
        Assert.DoesNotContain(Environment.UserName, unmarked, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), all, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("MY PRIVATE NOTE", all);
        Assert.DoesNotContain(FakeToken[..20], all);
        Assert.DoesNotContain("Secret Game", all);
        Assert.Contains("Note files: 1", all);
        Assert.Contains("<game 1>", all);
        Assert.DoesNotContain(files, f => f.Name == "games.txt");
    }

    [Fact]
    public void Game_names_are_included_only_when_asked_and_the_zip_is_written()
    {
        var files = DiagnosticsPackage.Build(paths, Input(true));
        Assert.Contains("Secret Game", files.Single(f => f.Name == "games.txt").Text);
        var output = Path.Combine(root, "out");
        Directory.CreateDirectory(output);
        var zip = DiagnosticsPackage.Create(paths, Input(false), output);
        using var archive = ZipFile.OpenRead(zip);
        Assert.Equal(new[] { "counts.txt", "environment.txt", "log.txt", "settings.json" }, archive.Entries.Select(e => e.FullName).Order().ToArray());
    }

    [Fact]
    public void A_missing_library_still_produces_a_package()
    {
        Directory.Delete(Path.Combine(root, "Profiles"), true);
        var files = DiagnosticsPackage.Build(paths, Input(false));
        Assert.Contains("Profile folders: 0", files.Single(f => f.Name == "counts.txt").Text);
    }

    [Fact]
    public void The_log_rolls_over_and_tails()
    {
        AppLog.Initialize(root);
        for (var i = 0; i < 4000; i++) AppLog.Info(new string('a', 100) + i);
        var file = AppLog.FilePath!;
        Assert.True(new FileInfo(file).Length <= AppLog.MaxBytes + 200);
        Assert.True(File.Exists(file + ".old"));
        Assert.Equal(50, AppLog.Tail(file, 50).Length);
    }
}
