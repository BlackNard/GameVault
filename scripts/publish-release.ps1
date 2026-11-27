# Publishes the release of the current main commit without anyone waiting (issue #107). Run only by .github/workflows/publish.yml, which the owner starts.
# It runs the release checks, creates the draft from their bundle, smoke-tests that draft and publishes it only when everything passed. On any failure the
# release stays unpublished and the draft this run created is removed, so it can simply be started again.
[CmdletBinding()]
param([string]$ReleaseName, [switch]$DryRun)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Import-Module (Join-Path $PSScriptRoot 'AppVersion.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'ReleasePackaging.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'ReleasePublishing.psm1') -Force
$root = Split-Path $PSScriptRoot -Parent

if ($env:GITHUB_ACTIONS -cne 'true' -or $env:GITHUB_REPOSITORY -cne 'BlackNard/GameVault') { throw 'Releases are published only by the Publish release workflow of BlackNard/GameVault.' }
if ($env:GITHUB_REF -cne 'refs/heads/main') { throw 'Start the workflow on the main branch.' }
if ($env:GITHUB_ACTOR -cne $env:GITHUB_REPOSITORY_OWNER -or $env:GITHUB_TRIGGERING_ACTOR -cne $env:GITHUB_REPOSITORY_OWNER) { throw 'Only the repository owner publishes releases.' }
if ([string]::IsNullOrWhiteSpace($env:GH_TOKEN) -or $env:GITHUB_SHA -notmatch '^[a-f0-9]{40}$') { throw 'The workflow did not provide its token and commit.' }

$repository = $env:GITHUB_REPOSITORY
$api = 'https://api.github.com/repos/' + $repository
$headers = @{ Authorization = 'Bearer ' + $env:GH_TOKEN; Accept = 'application/vnd.github+json'; 'User-Agent' = 'GameVault-Publish' }
$version = Get-AppVersion $root
$tag = "v$version"

# ---------- everything that can be checked before the long run ----------
$title = Get-ReleaseTitle $version $ReleaseName
$notes = Get-ReleaseNoteLines ([IO.File]::ReadAllText((Join-Path $root 'src/GameVault.Core/ReleaseNotes.cs'))) $version
$releases = Invoke-RestMethod "$api/releases?per_page=100" -Headers $headers
$refs = Invoke-RestMethod ("$api/git/matching-refs/tags/" + [Uri]::EscapeDataString($tag)) -Headers $headers
Assert-TagAvailable -Releases $releases -Refs $refs -Tag $tag
Assert-NewerVersion $releases $version
Write-Host "Publishing $tag as '$title' from $($env:GITHUB_SHA)."

function Get-BuildRuns { @((Invoke-RestMethod "$api/actions/workflows/build.yml/runs?event=workflow_dispatch&branch=main&per_page=30" -Headers $headers).workflow_runs) }

# Starts build.yml and returns the run it started.
function Start-Build([hashtable]$Inputs, [string]$Commit) {
    $known = @(Get-BuildRuns | ForEach-Object { [long]$_.id })
    $body = @{ ref = 'main'; inputs = $Inputs } | ConvertTo-Json
    Invoke-RestMethod "$api/actions/workflows/build.yml/dispatches" -Headers $headers -Method Post -ContentType 'application/json' -Body $body | Out-Null
    for ($try = 0; $try -lt 30; $try++) {
        Start-Sleep -Seconds 10
        $run = Select-DispatchedRun (Get-BuildRuns) $known $Commit
        if ($run) { Write-Host "Started $($run.html_url)"; return [long]$run.id }
    }
    throw 'The build did not start. If main changed while this was running, start the publish again.'
}

# Waits for the run; failed jobs are run once more, because some tests are known to fail by timing on busy runners.
function Wait-Build([long]$Id) {
    $retried = $false
    $deadline = [DateTime]::UtcNow.AddMinutes(75)
    while ([DateTime]::UtcNow -lt $deadline) {
        Start-Sleep -Seconds 30
        try { $run = Invoke-RestMethod "$api/actions/runs/$Id" -Headers $headers }
        catch { Write-Host "GitHub did not answer; asking again. $($_.Exception.Message)"; continue }
        if ($run.status -cne 'completed') { continue }
        if ($run.conclusion -ceq 'success') { return }
        if ($retried) { throw "The checks failed twice: $($run.html_url)" }
        Write-Host "The checks ended with '$($run.conclusion)'; running the failed jobs once more."
        Invoke-RestMethod "$api/actions/runs/$Id/rerun-failed-jobs" -Headers $headers -Method Post | Out-Null
        $retried = $true
        Start-Sleep -Seconds 30
    }
    throw 'The checks did not finish in time.'
}

# ---------- the release checks ----------
$buildId = Start-Build @{} $env:GITHUB_SHA
Wait-Build $buildId

$work = Join-Path $root ('artifacts/publish-' + [guid]::NewGuid().ToString('N'))
$bundle = Join-Path $work 'bundle'
New-Item -ItemType Directory $bundle -Force | Out-Null
$artifacts = @((Invoke-RestMethod "$api/actions/runs/$buildId/artifacts?name=private-draft-assets" -Headers $headers).artifacts)
if ($artifacts.Count -ne 1) { throw 'The checks did not leave exactly one release bundle.' }
$archive = Join-Path $work 'bundle.zip'
Invoke-WebRequest "$api/actions/artifacts/$($artifacts[0].id)/zip" -Headers $headers -OutFile $archive
Expand-Archive -LiteralPath $archive -DestinationPath $bundle
$manifestFile = @(Get-ChildItem -LiteralPath $bundle -Recurse -Filter 'RELEASE-MANIFEST.json')
if ($manifestFile.Count -ne 1) { throw 'The release bundle has no single manifest.' }
$bundle = $manifestFile[0].DirectoryName

# ---------- draft, smoke, publish ----------
$draftId = $null
$published = $false
try {
    $env:GAMEVAULT_PUBLIC_REPOSITORY_APPROVED = 'true'      # starting this workflow is the owner's approval
    & (Join-Path $PSScriptRoot 'create-draft-release.ps1') -BundleDirectory $bundle -Tag $tag | Write-Host
    $draftId = (Get-Content -LiteralPath (Join-Path $root 'artifacts/draft-release.json') -Raw | ConvertFrom-Json).releaseId

    Wait-Build (Start-Build @{ draft_release_id = "$draftId" } '')

    $evidence = "https://github.com/$repository/actions/runs/$buildId"
    if ($DryRun) {
        Write-Host "Dry run: every check passed. '$title' would be published with these notes:`n"
        Write-Host (Get-ReleaseBody $notes $version $evidence)
    }
    else {
        $body = @{ name = $title; body = (Get-ReleaseBody $notes $version $evidence); draft = $false; prerelease = $true; make_latest = 'false' } | ConvertTo-Json
        $release = Invoke-RestMethod "$api/releases/$draftId" -Headers $headers -Method Patch -ContentType 'application/json; charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes($body))
        if ($release.draft -ne $false -or $null -eq $release.published_at) { throw 'GitHub did not publish the release.' }
        $published = $true
        Write-Host "Published: $($release.html_url)"
        if ($env:GITHUB_STEP_SUMMARY) { "Published [$title]($($release.html_url))" | Add-Content -LiteralPath $env:GITHUB_STEP_SUMMARY }
    }
}
finally {
    # An unpublished draft left behind would block the next attempt (and a dry run must leave nothing).
    if (-not $published) {
        if (-not $draftId) {
            $left = @((Invoke-RestMethod "$api/releases?per_page=100" -Headers $headers) | Where-Object { $_.tag_name -ceq $tag -and $_.draft -eq $true })
            if ($left.Count -eq 1) { $draftId = $left[0].id }
        }
        if ($draftId) {
            $current = Invoke-RestMethod "$api/releases/$draftId" -Headers $headers
            if ($current.draft -eq $true) {
                Invoke-RestMethod "$api/releases/$draftId" -Headers $headers -Method Delete | Out-Null
                Write-Host 'The unpublished draft of this run was removed.'
            }
        }
    }
}
