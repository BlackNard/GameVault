using System.Diagnostics;
using System.Text;

namespace GameVault.Tests;

/// <summary>
/// Runs the real scripts/publish-release.ps1 against a pretend GitHub in a temporary folder (issue #107): nothing is sent anywhere and nothing waits.
/// The pretend create-draft-release.ps1 only makes the draft appear.
/// </summary>
public sealed class PublishFlowTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "GameVault publish", Guid.NewGuid().ToString("N"));

    public void Dispose() { try { Directory.Delete(root, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }

    private const string PretendGitHub = """
        $ErrorActionPreference = 'Stop'
        $global:runs = [Collections.ArrayList]::new()
        $global:releases = [Collections.ArrayList]::new()
        $global:results = [Collections.Queue]::new([object[]]($env:RESULTS -split ','))
        $global:conclusions = @{}
        function Start-Sleep { }
        function Expand-Archive { param($LiteralPath, $DestinationPath) Set-Content (Join-Path $DestinationPath 'RELEASE-MANIFEST.json') '{}' }
        function Invoke-WebRequest { param($Uri, $Headers, $OutFile) Set-Content $OutFile 'zip' }
        function Invoke-RestMethod {
            param([Parameter(Position = 0)]$Uri, $Headers, $Method = 'Get', $ContentType, $Body)
            if ($Uri -match '/dispatches$') {
                $inputs = ($Body | ConvertFrom-Json).inputs
                $kind = if (@($inputs.PSObject.Properties | ForEach-Object { $_.Name }) -contains 'draft_release_id') { 'smoke' } else { 'checks' }
                $id = 100 + $global:runs.Count
                [void]$global:runs.Add([pscustomobject]@{ id = $id; head_sha = $env:GITHUB_SHA; html_url = "run/$id" })
                $global:conclusions[[string]$id] = [string]$global:results.Dequeue()
                Write-Host "LOG start $kind"
                return
            }
            if ($Uri -match '/workflows/build.yml/runs') { return [pscustomobject]@{ workflow_runs = @($global:runs) } }
            if ($Uri -match '/runs/(\d+)/rerun-failed-jobs$') { Write-Host 'LOG rerun'; $global:conclusions[$Matches[1]] = [string]$global:results.Dequeue(); return }
            if ($Uri -match '/runs/(\d+)/artifacts') { return [pscustomobject]@{ artifacts = @([pscustomobject]@{ id = 1 }) } }
            if ($Uri -match '/runs/(\d+)$') { return [pscustomobject]@{ status = 'completed'; conclusion = $global:conclusions[$Matches[1]]; html_url = "run/$($Matches[1])" } }
            if ($Uri -match '/matching-refs/') { return @() }
            if ($Uri -match '/releases/(\d+)$') {
                $release = @($global:releases | Where-Object { $_.id -eq [int]$Matches[1] })[0]
                if ($Method -eq 'Patch') {
                    $change = [Text.Encoding]::UTF8.GetString($Body) | ConvertFrom-Json
                    $release.draft = $change.draft; $release.published_at = 'now'
                    Write-Host "LOG publish name=$($change.name) prerelease=$($change.prerelease) latest=$($change.make_latest)"
                }
                if ($Method -eq 'Delete') { $global:releases.Remove($release); Write-Host 'LOG delete'; return }
                return $release
            }
            if ($Uri -match '/releases\?') { return , @($global:releases) }
            throw "Unexpected request: $Method $Uri"
        }
        try { & (Join-Path $PSScriptRoot 'scripts/publish-release.ps1') -DryRun:($env:DRY -eq 'true') } catch { Write-Host "LOG failed $($_.Exception.Message)" }
        Write-Host "LOG left $(@($global:releases | Where-Object draft -eq $true).Count) draft"
        """;

    private const string PretendDraft = """
        param($BundleDirectory, $Tag)
        [void]$global:releases.Add([pscustomobject]@{ id = 77; draft = $true; published_at = $null; tag_name = $Tag; html_url = 'release/77' })
        Write-Host 'LOG draft'
        New-Item -ItemType Directory (Join-Path (Split-Path $PSScriptRoot -Parent) 'artifacts') -Force | Out-Null
        Set-Content (Join-Path (Split-Path $PSScriptRoot -Parent) 'artifacts/draft-release.json') '{"releaseId":77}'
        """;

    private string[] Publish(string results, bool dry = false, string actor = "owner")
    {
        var source = new DirectoryInfo(AppContext.BaseDirectory);
        while (source is not null && !File.Exists(Path.Combine(source.FullName, "GameVault.slnx"))) source = source.Parent;
        Directory.CreateDirectory(Path.Combine(root, "scripts"));
        Directory.CreateDirectory(Path.Combine(root, "src", "GameVault.Core"));
        File.Copy(Path.Combine(source!.FullName, "Directory.Build.props"), Path.Combine(root, "Directory.Build.props"), true);
        File.Copy(Path.Combine(source.FullName, "src", "GameVault.Core", "ReleaseNotes.cs"), Path.Combine(root, "src", "GameVault.Core", "ReleaseNotes.cs"), true);
        foreach (var name in new[] { "publish-release.ps1", "AppVersion.psm1", "ReleasePackaging.psm1", "ReleasePublishing.psm1", "InstallerPayload.psm1" })
            File.Copy(Path.Combine(source.FullName, "scripts", name), Path.Combine(root, "scripts", name), true);
        File.WriteAllText(Path.Combine(root, "scripts", "create-draft-release.ps1"), PretendDraft);
        File.WriteAllText(Path.Combine(root, "github.ps1"), PretendGitHub);
        var info = new ProcessStartInfo("pwsh") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, WorkingDirectory = root, StandardOutputEncoding = Encoding.UTF8 };
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-File", Path.Combine(root, "github.ps1") }) info.ArgumentList.Add(argument);
        foreach (var (key, value) in new[]
        {
            ("GITHUB_ACTIONS", "true"), ("GITHUB_REPOSITORY", "BlackNard/GameVault"), ("GITHUB_REF", "refs/heads/main"), ("GITHUB_ACTOR", actor), ("GITHUB_TRIGGERING_ACTOR", actor),
            ("GITHUB_REPOSITORY_OWNER", "owner"), ("GH_TOKEN", "pretend"), ("GITHUB_SHA", new string('a', 40)), ("RESULTS", results), ("DRY", dry ? "true" : "false"), ("GITHUB_STEP_SUMMARY", "")
        }) info.Environment[key] = value;
        using var process = Process.Start(info)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        Assert.True(process.WaitForExit(60_000), "the script did not end");
        var all = output.Result + error.Result;
        var log = all.Split('\n').Select(l => l.Trim()).Where(l => l.StartsWith("LOG ", StringComparison.Ordinal)).Select(l => l[4..]).ToArray();
        Assert.True(log.Length > 0, all);
        return log;
    }

    [Fact]
    public void GreenChecksArePublishedAsAPrereleaseThatIsNotLatest()
    {
        var log = Publish("success,success");
        Assert.Equal(["start checks", "draft", "start smoke", "publish name=GameVault 0.1.0 — Update 8 prerelease=True latest=false", "left 0 draft"],
            log.Select(l => System.Text.RegularExpressions.Regex.Replace(l, @"Update \d+", "Update 8")));
    }

    [Fact]
    public void AFlakyCheckIsRunOnceMore() =>
        Assert.Contains(Publish("failure,success,success"), l => l.StartsWith("publish ", StringComparison.Ordinal));

    [Fact]
    public void ChecksThatFailTwiceCreateNothing()
    {
        var log = Publish("failure,failure");
        Assert.Equal(["start checks", "rerun"], log.Take(2));
        Assert.StartsWith("failed The checks failed twice", log[2]);
        Assert.Equal("left 0 draft", log[^1]);
        Assert.DoesNotContain("draft", log);
    }

    [Fact]
    public void AFailedSmokeTestRemovesTheDraftAndPublishesNothing()
    {
        var log = Publish("success,failure,failure");
        Assert.Contains("delete", log);
        Assert.DoesNotContain(log, l => l.StartsWith("publish ", StringComparison.Ordinal));
        Assert.Equal("left 0 draft", log[^1]);
    }

    [Fact]
    public void ADryRunChecksEverythingAndLeavesNothing()
    {
        var log = Publish("success,success", dry: true);
        Assert.Equal(["start checks", "draft", "start smoke", "delete", "left 0 draft"], log);
    }

    [Fact]
    public void SomeoneElseStartsNothing()
    {
        var log = Publish("success,success", actor: "someone");
        Assert.StartsWith("failed Only the repository owner", log[0]);
        Assert.DoesNotContain("start checks", log);
    }
}
