# Creates only an unpublished development draft; never publishes/tags. Works on a private repository, or on a public one when the owner confirms it with GAMEVAULT_PUBLIC_REPOSITORY_APPROVED=true.
[CmdletBinding()]
param([Parameter(Mandatory)][string]$BundleDirectory,
      [string]$Tag)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
Import-Module (Join-Path $PSScriptRoot 'ReleasePackaging.psm1') -Force
Assert-ReleaseBundle $BundleDirectory
$root=Split-Path $PSScriptRoot -Parent
$manifest=Get-Content -LiteralPath (Join-Path $BundleDirectory 'RELEASE-MANIFEST.json') -Raw|ConvertFrom-Json
if([string]::IsNullOrWhiteSpace($env:GH_TOKEN)){throw 'Set GH_TOKEN for authorized draft access.'}
$repository='BlackNard/GameVault'
$api='https://api.github.com/repos/'+$repository
$headers=@{Authorization='Bearer '+$env:GH_TOKEN;Accept='application/vnd.github+json';'User-Agent'='GameVault-Private-Draft'}
$repo=Invoke-RestMethod $api -Headers $headers
if($repo.private -ne $true -and $env:GAMEVAULT_PUBLIC_REPOSITORY_APPROVED -cne 'true'){throw 'This repository is public. Set GAMEVAULT_PUBLIC_REPOSITORY_APPROVED=true to confirm the owner authorized releases from it. The tool still only creates an unpublished draft.'}
$origin=Assert-ReleaseOrigin -BundleDirectory $BundleDirectory -Api $api -Headers $headers
if($manifest.checks.appWorkflowsPassed -ne $true -or $manifest.checks.installerLifecyclePassed -ne $true -or $manifest.checks.sourceHistoryAuditPassed -ne $true){throw 'Missing passed release checks.'}
$commit=Invoke-RestMethod ($api+'/commits/'+$manifest.sourceCommit) -Headers $headers
if($manifest.sourceCommit -cne $manifest.headCommit -and $manifest.headCommit -cnotin @($commit.parents.sha)){throw 'Release source is not the matching branch commit/PR merge.'}
if([string]::IsNullOrWhiteSpace($Tag)){$Tag='v'+$manifest.version}
if($Tag -cne ('v'+$manifest.version)){throw 'The tag must be v followed by the version of the bundle.'}
$existing=Invoke-RestMethod ($api+'/releases?per_page=100') -Headers $headers
$refs=Invoke-RestMethod ($api+'/git/matching-refs/tags/'+[Uri]::EscapeDataString($Tag)) -Headers $headers
Assert-TagAvailable -Releases $existing -Refs $refs -Tag $Tag
$notes=[IO.File]::ReadAllText((Join-Path $BundleDirectory 'RELEASE-NOTES.md'))+"`n`nBuild evidence: https://github.com/$repository/actions/runs/$($manifest.buildRunId)`n"
$body=@{tag_name=$Tag;target_commitish=$manifest.sourceCommit;name=('GameVault '+$manifest.version);body=$notes;draft=$true;prerelease=$true;make_latest='false'}|ConvertTo-Json -Depth 4
$release=Invoke-RestMethod ($api+'/releases') -Headers $headers -Method Post -ContentType 'application/json; charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes($body))
if($release.draft -ne $true -or $null -ne $release.published_at){throw 'GitHub did not return an unpublished draft.'}
$id=$release.id
foreach($name in Get-ReleaseAssetNames $manifest.version){
    $path=Join-Path $BundleDirectory $name
    $upload='https://uploads.github.com/repos/'+$repository+'/releases/'+$id+'/assets?name='+[Uri]::EscapeDataString($name)
    $asset=Invoke-RestMethod $upload -Headers $headers -Method Post -ContentType 'application/octet-stream' -InFile $path
    if($asset.state -cne 'uploaded' -or $asset.size -ne (Get-Item -LiteralPath $path).Length){throw 'Draft asset upload was incomplete.'}
}
$release=Invoke-RestMethod ($api+'/releases/'+$id) -Headers $headers
if($release.draft -ne $true -or $null -ne $release.published_at){throw 'Draft state changed unexpectedly.'}
$download=Join-Path $root ('artifacts/draft-download-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory $download|Out-Null
foreach($name in Get-ReleaseAssetNames $manifest.version){
    $assets=@($release.assets|Where-Object name -ceq $name)
    if($assets.Count -ne 1){throw 'Missing/duplicate uploaded draft asset.'}
    $binaryHeaders=@{Authorization=$headers.Authorization;Accept='application/octet-stream';'User-Agent'=$headers.'User-Agent'}
    Invoke-WebRequest ($api+'/releases/assets/'+$assets[0].id) -Headers $binaryHeaders -OutFile (Join-Path $download $name)
}
Assert-ReleaseBundle $download
$result=[ordered]@{releaseId=$id;url=$release.html_url;tag=$Tag;draft=$true;published=$false;repositoryPrivate=($repo.private -eq $true);sourceCommit=$manifest.sourceCommit;headCommit=$manifest.headCommit;buildRunId=$manifest.buildRunId;installerSha256=$manifest.installerSha256;downloadDirectory=$download;downloadChecksumsVerified=$true;originArtifactId=$origin.artifactId}
$result|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $root 'artifacts/draft-release.json') -Encoding utf8
[pscustomobject]@{releaseId=$id;url=$release.html_url;draft=$true;published=$false;downloadChecksumsVerified=$true;originArtifactId=$origin.artifactId}|ConvertTo-Json
