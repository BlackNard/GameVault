# Creates only an unpublished private development draft; never publishes/tags.
[CmdletBinding()]
param([Parameter(Mandatory)][string]$BundleDirectory,
      [string]$Tag)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
Import-Module (Join-Path $PSScriptRoot 'ReleasePackaging.psm1') -Force
Assert-ReleaseBundle $BundleDirectory
$root=Split-Path $PSScriptRoot -Parent
$manifest=Get-Content -LiteralPath (Join-Path $BundleDirectory 'RELEASE-MANIFEST.json') -Raw|ConvertFrom-Json
if([string]::IsNullOrWhiteSpace($env:GH_TOKEN)){throw 'Set GH_TOKEN for authorized private draft access.'}
$repository='Zaid-Alhmouz/GameVault'
$api='https://api.github.com/repos/'+$repository
$headers=@{Authorization='Bearer '+$env:GH_TOKEN;Accept='application/vnd.github+json';'User-Agent'='GameVault-Private-Draft'}
$repo=Invoke-RestMethod $api -Headers $headers
if($repo.private -ne $true){throw 'Only a private repository is authorized by this draft tool.'}
$origin=Assert-ReleaseOrigin -BundleDirectory $BundleDirectory -Api $api -Headers $headers
if($manifest.checks.appWorkflowsPassed -ne $true -or $manifest.checks.installerLifecyclePassed -ne $true -or $manifest.checks.sourceHistoryAuditPassed -ne $true){throw 'Missing passed release checks.'}
$commit=Invoke-RestMethod ($api+'/commits/'+$manifest.sourceCommit) -Headers $headers
if($manifest.sourceCommit -cne $manifest.headCommit -and $manifest.headCommit -cnotin @($commit.parents.sha)){throw 'Release source is not the matching branch commit/PR merge.'}
if([string]::IsNullOrWhiteSpace($Tag)){$Tag='v'+$manifest.version+'-preview.1'}
if($Tag -notmatch ('^v'+[regex]::Escape($manifest.version)+'-preview\.[1-9][0-9]*$')){throw 'Only an explicit versioned preview tag is permitted.'}
$existing=Invoke-RestMethod ($api+'/releases?per_page=100') -Headers $headers
$refs=Invoke-RestMethod ($api+'/git/matching-refs/tags/'+[Uri]::EscapeDataString($Tag)) -Headers $headers
Assert-PreviewTagAvailable -Releases $existing -Refs $refs -Tag $Tag
$notes=[IO.File]::ReadAllText((Join-Path $BundleDirectory 'RELEASE-NOTES.md'))+"`n`nBuild evidence: https://github.com/$repository/actions/runs/$($manifest.buildRunId)`n"
$body=@{tag_name=$Tag;target_commitish=$manifest.sourceCommit;name=('GameVault '+$manifest.version+' — Development preview');body=$notes;draft=$true;prerelease=$true;make_latest='false'}|ConvertTo-Json -Depth 4
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
$result=[ordered]@{releaseId=$id;url=$release.html_url;tag=$Tag;draft=$true;published=$false;repositoryPrivate=$true;sourceCommit=$manifest.sourceCommit;headCommit=$manifest.headCommit;buildRunId=$manifest.buildRunId;installerSha256=$manifest.installerSha256;downloadDirectory=$download;downloadChecksumsVerified=$true;originArtifactId=$origin.artifactId}
$result|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $root 'artifacts/draft-release.json') -Encoding utf8
[pscustomobject]@{releaseId=$id;url=$release.html_url;draft=$true;published=$false;downloadChecksumsVerified=$true;originArtifactId=$origin.artifactId}|ConvertTo-Json
