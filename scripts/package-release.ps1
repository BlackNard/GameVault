[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$root=Split-Path $PSScriptRoot -Parent
Import-Module (Join-Path $PSScriptRoot 'ReleasePackaging.psm1') -Force
$artifacts=Join-Path $root 'artifacts'
$verification=Get-Content -LiteralPath (Join-Path $artifacts 'verification.json') -Raw|ConvertFrom-Json
$validation=Get-Content -LiteralPath (Join-Path $artifacts 'validation.json') -Raw|ConvertFrom-Json
$lifecycle=Get-Content -LiteralPath (Join-Path $artifacts 'installer-lifecycle.json') -Raw|ConvertFrom-Json
$installer=Get-Content -LiteralPath (Join-Path $artifacts 'installer.json') -Raw|ConvertFrom-Json
$manifest=Get-Content -LiteralPath $installer.manifest -Raw|ConvertFrom-Json
$audit=Get-Content -LiteralPath (Join-Path $artifacts 'source-audit.json') -Raw|ConvertFrom-Json
Assert-ReleaseEvidence $verification $validation $lifecycle $manifest $installer.installer
$source=(& git -C $root rev-parse HEAD).Trim()
if($LASTEXITCODE -ne 0 -or $source -notmatch '^[a-f0-9]{40}$'){throw 'Invalid source identity.'}
if($audit.schemaVersion -ne 1 -or $audit.passed -ne $true -or $audit.workingTreeClean -ne $true -or $audit.commitSha -cne $source){throw 'A matching clean source-history audit is required.'}
if($env:GITHUB_RUN_ID -notmatch '^\d+$' -or $env:GAMEVAULT_HEAD_SHA -notmatch '^[a-f0-9]{40}$'){throw 'Release packaging requires identified CI provenance.'}
$directory=Join-Path $artifacts ('release-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory $directory|Out-Null
$name=[IO.Path]::GetFileName($installer.installer)
Copy-Item -LiteralPath $installer.installer -Destination (Join-Path $directory $name)
($manifest.sha256+'  '+$name)|Set-Content -LiteralPath (Join-Path $directory ($name+'.sha256')) -Encoding ascii
foreach($template in @('INSTALL','RELEASE-NOTES')){
    $text=[IO.File]::ReadAllText((Join-Path $root ('release/'+$template+'.template.md'))).Replace('@@VERSION@@',$manifest.appVersion)
    [IO.File]::WriteAllText((Join-Path $directory ($template+'.md')),$text,[Text.UTF8Encoding]::new($false))
}
Copy-Item -LiteralPath (Join-Path $verification.publishDirectory 'THIRD-PARTY-NOTICES.txt') -Destination (Join-Path $directory 'THIRD-PARTY-NOTICES.txt')
Copy-Item -LiteralPath (Join-Path $verification.publishDirectory 'licenses/dotnet/LICENSE.txt') -Destination (Join-Path $directory 'DOTNET-LICENSE.txt')
Copy-Item -LiteralPath (Join-Path $verification.publishDirectory 'licenses/dotnet/THIRD-PARTY-NOTICES.txt') -Destination (Join-Path $directory 'DOTNET-THIRD-PARTY-NOTICES.txt')
$public=[ordered]@{schemaVersion=1;version=$manifest.appVersion;architecture='win-x64';installerFile=$name;installerSha256=$manifest.sha256;
    sourceCommit=$source;headCommit=$env:GAMEVAULT_HEAD_SHA;buildRunId=$env:GITHUB_RUN_ID;verificationRunId=$verification.runId;
    signed=$false;releaseApproved=$false;manualAcceptance='pending-owner-final-pass';projectLicense='source-available-all-rights-reserved';publicDistribution='not-authorized';
    checks=@{appWorkflowsPassed=$true;installerLifecyclePassed=$true;sourceHistoryAuditPassed=$true};
    payload=@($manifest.payload|ForEach-Object{[ordered]@{path=$_.path;sha256=$_.sha256}})}
$public|ConvertTo-Json -Depth 6|Set-Content -LiteralPath (Join-Path $directory 'RELEASE-MANIFEST.json') -Encoding utf8
Get-ChildItem -LiteralPath $directory -File|Sort-Object Name|ForEach-Object{(Get-FileHash -LiteralPath $_.FullName).Hash.ToLowerInvariant()+'  '+$_.Name}|
    Set-Content -LiteralPath (Join-Path $directory 'SHA256SUMS.txt') -Encoding ascii
Assert-ReleaseBundle $directory
[ordered]@{directory=$directory;sourceCommit=$source;headCommit=$env:GAMEVAULT_HEAD_SHA;version=$manifest.appVersion;installerSha256=$manifest.sha256;releaseApproved=$false}|
    ConvertTo-Json|Set-Content -LiteralPath (Join-Path $artifacts 'release.json') -Encoding utf8
Write-Host "Verified private draft assets prepared: $directory"
