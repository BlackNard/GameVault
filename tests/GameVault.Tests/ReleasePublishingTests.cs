using System.Diagnostics;
using System.Text;
using System.Text.Json;
using GameVault.Core;

namespace GameVault.Tests;

/// <summary>One-click publishing (issue #107): the parts of scripts/ReleasePublishing.psm1 that need no GitHub, and the workflow's guards.</summary>
public sealed class ReleasePublishingTests
{
    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "GameVault.slnx"))) dir = dir.Parent;
        return dir!.FullName;
    }

    private static string File_(params string[] parts) => File.ReadAllText(Path.Combine([Root(), .. parts]));

    private static (int Code, string Output) Pwsh(string command)
    {
        var module = Path.Combine(Root(), "scripts", "ReleasePublishing.psm1");
        var script = $"$ErrorActionPreference='Stop'; [Console]::OutputEncoding=[Text.Encoding]::UTF8; Import-Module '{module}' -Force; {command}";
        var info = new ProcessStartInfo("pwsh") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, StandardOutputEncoding = Encoding.UTF8 };
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(script)) }) info.ArgumentList.Add(argument);
        using var process = Process.Start(info)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        return (process.ExitCode, process.ExitCode == 0 ? output.Result : error.Result + output.Result);
    }

    [Fact]
    public void TheNotesOnGitHubAreTheOnesTheAppShows()
    {
        var source = Path.Combine(Root(), "src", "GameVault.Core", "ReleaseNotes.cs");
        foreach (var note in ReleaseNotes.All)
        {
            var (code, output) = Pwsh($"Get-ReleaseNoteLines ([IO.File]::ReadAllText('{source}')) '{note.Version}' | ConvertTo-Json -Depth 3");
            Assert.True(code == 0, output);
            using var json = JsonDocument.Parse(output);
            Assert.Equal(note.New, json.RootElement.GetProperty("New").EnumerateArray().Select(e => e.GetString()));
            Assert.Equal(note.Fixed, json.RootElement.GetProperty("Fixed").EnumerateArray().Select(e => e.GetString()));
        }
        Assert.NotEqual(0, Pwsh($"Get-ReleaseNoteLines ([IO.File]::ReadAllText('{source}')) '9.9.9-preview.1'").Code);
    }

    [Fact]
    public void TheReleaseNameNeverSaysPreview()
    {
        var (code, output) = Pwsh("Get-ReleaseTitle '0.1.0' '8' ''; Get-ReleaseTitle '0.1.0' '8' ' Journal and lists '");
        Assert.True(code == 0, output);
        Assert.Equal(["GameVault 0.1.0 — Update 8", "Journal and lists"], output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        Assert.NotEqual(0, Pwsh("Get-ReleaseTitle '0.1.0' '8' 'GameVault Preview 8'").Code);
    }

    [Fact]
    public void ThePageListsWhatIsNewAndFixedAndHowToUpdate()
    {
        var (code, output) = Pwsh("Get-ReleaseBody ([pscustomobject]@{New=@('A thing');Fixed=@()}) '0.1.0' 'https://example.test/run'");
        Assert.True(code == 0, output);
        Assert.Contains("## What's new", output);
        Assert.Contains("- A thing", output);
        Assert.DoesNotContain("## Fixed", output);
        Assert.Contains("GameVault-Setup-0.1.0-win-x64.exe", output);
        Assert.Contains("https://example.test/run", output);
    }

    [Fact]
    public void OnlyANewRunOfTheWantedCommitIsFollowed()
    {
        const string runs = "@([pscustomobject]@{id=5;head_sha='a'},[pscustomobject]@{id=9;head_sha='b'},[pscustomobject]@{id=8;head_sha='a'},[pscustomobject]@{id=7;head_sha='a'})";
        var (code, output) = Pwsh($"(Select-DispatchedRun {runs} @(5) 'a').id; (Select-DispatchedRun {runs} @(5,7,8) '').id; [string](Select-DispatchedRun {runs} @(5,7,8) 'a')");
        Assert.True(code == 0, output);
        Assert.Equal(["7", "9"], output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    [Fact]
    public void OnlyTheOwnerStartsItByHandAndAFailureLeavesNothingPublished()
    {
        var workflow = File_(".github", "workflows", "publish.yml").Replace("\r\n", "\n");
        var triggers = workflow[workflow.IndexOf("\non:", StringComparison.Ordinal)..workflow.IndexOf("\npermissions:", StringComparison.Ordinal)];
        Assert.Contains("workflow_dispatch:", triggers);
        foreach (var other in new[] { "push:", "pull_request", "schedule:", "workflow_run" }) Assert.DoesNotContain(other, triggers);
        Assert.Contains("github.actor == github.repository_owner && github.triggering_actor == github.repository_owner", workflow);
        var script = File_("scripts", "publish-release.ps1");
        Assert.True(script.IndexOf("$env:GITHUB_ACTOR -cne $env:GITHUB_REPOSITORY_OWNER", StringComparison.Ordinal) < script.IndexOf("Start-Build @{}", StringComparison.Ordinal));
        var checks = script.IndexOf("Wait-Build $buildId", StringComparison.Ordinal);
        var draft = script.IndexOf("'create-draft-release.ps1'", StringComparison.Ordinal);
        var smoke = script.IndexOf("Wait-Build (Start-Build @{ draft_release_id", StringComparison.Ordinal);
        var publish = script.IndexOf("draft = $false", StringComparison.Ordinal);
        Assert.True(checks > 0 && checks < draft && draft < smoke && smoke < publish, "Checks, draft, smoke test, then publish.");
        Assert.Contains("-Method Delete", script);
        Assert.Contains("if ($current.draft -eq $true)", script);          // a published release is never removed
    }
}
