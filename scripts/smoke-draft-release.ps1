# Executes downloaded setup only on disposable GitHub-hosted Windows.
[CmdletBinding()]
param([Parameter(Mandatory)][ValidatePattern('^[1-9][0-9]*$')][string]$ReleaseId)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
Import-Module (Join-Path $PSScriptRoot 'InstallerLifecycle.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'ReleasePackaging.psm1') -Force
Assert-DisposableInstallerRunner
$root=Split-Path $PSScriptRoot -Parent
if([string]::IsNullOrWhiteSpace($env:GH_TOKEN) -or $env:GITHUB_REPOSITORY -cne 'Zaid-Alhmouz/GameVault'){throw 'Authorized GameVault draft access is required.'}
$headers=@{Authorization='Bearer '+$env:GH_TOKEN;Accept='application/vnd.github+json';'User-Agent'='GameVault-Draft-Smoke'}
$api='https://api.github.com/repos/'+$env:GITHUB_REPOSITORY
$repository=Invoke-RestMethod ($api) -Headers $headers
if($repository.private -ne $true -and $env:GAMEVAULT_PUBLIC_REPOSITORY_APPROVED -cne 'true'){throw 'Draft smoke on a public repository needs GAMEVAULT_PUBLIC_REPOSITORY_APPROVED=true (set by the owner-approved workflow).'}
$release=Invoke-RestMethod ($api+'/releases/'+$ReleaseId) -Headers $headers
if($release.draft -ne $true -or $null -ne $release.published_at){throw 'Only unpublished draft releases can be tested by this workflow.'}
$token=[guid]::NewGuid().ToString('N')
$sandbox=Join-Path ([IO.Path]::GetFullPath($env:RUNNER_TEMP)) ('GameVault draft smoke '+$token)
New-Item -ItemType Directory $sandbox|Out-Null
$downloads=Join-Path $sandbox 'Downloaded release';New-Item -ItemType Directory $downloads|Out-Null
function Download-Asset([string]$Name){
    $assets=@($release.assets|Where-Object name -ceq $Name)
    if($assets.Count -ne 1 -or $assets[0].state -cne 'uploaded' -or $assets[0].size -gt 250MB){throw 'Missing/invalid draft asset.'}
    $asset=$assets[0]
    $binaryHeaders=@{Authorization=$headers.Authorization;Accept='application/octet-stream';'User-Agent'=$headers.'User-Agent'}
    $path=Join-Path $downloads $Name
    Invoke-WebRequest ($api+'/releases/assets/'+$asset.id) -Headers $binaryHeaders -OutFile $path
    if((Get-Item -LiteralPath $path).Length -ne $asset.size){throw 'Downloaded asset size differs from GitHub metadata.'}
    return $asset
}
$null=Download-Asset 'RELEASE-MANIFEST.json'
$manifest=Get-Content -LiteralPath (Join-Path $downloads 'RELEASE-MANIFEST.json') -Raw|ConvertFrom-Json
$names=@(Get-ReleaseAssetNames $manifest.version)
if(@($release.assets).Count -ne $names.Count){throw 'Draft contains unexpected assets.'}
foreach($name in $names){if($name -cne 'RELEASE-MANIFEST.json'){$null=Download-Asset $name}}
Assert-ReleaseBundle $downloads
$origin=Assert-ReleaseOrigin -BundleDirectory $downloads -Api $api -Headers $headers
if($release.target_commitish -cne $manifest.sourceCommit){throw 'Draft target does not match its packaged source commit.'}
$commit=Invoke-RestMethod ($api+'/commits/'+$manifest.sourceCommit) -Headers $headers
if($manifest.sourceCommit -cne $manifest.headCommit -and $manifest.headCommit -cnotin @($commit.parents.sha)){throw 'Source commit is not the matching branch commit or its checked-out PR merge.'}
$registration='HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{11DE0F05-D06D-4594-A970-85DDE91DC106}_is1'
if(Test-Path -LiteralPath $registration){throw 'Pre-existing GameVault registration; refusing smoke.'}
$installation=Join-Path $sandbox 'Installed GameVault'
New-Item -ItemType Directory $installation|Out-Null
[IO.File]::WriteAllText((Join-Path $installation '.installer-fixture-token'),$token)
$evidence=Join-Path $root 'artifacts/draft-asset-smoke';New-Item -ItemType Directory $evidence -Force|Out-Null
$setup=Join-Path $downloads $manifest.installerFile
$log=Join-Path $evidence 'install.log'
$arguments='/VERYSILENT /SUPPRESSMSGBOXES /SP- /NORESTART /NOCLOSEAPPLICATIONS /NORESTARTAPPLICATIONS /TASKS="" /DIR="'+$installation+'" /LOG="'+$log+'"'
# API access is complete; child installers/fixtures must not inherit the token.
Remove-Item Env:GH_TOKEN -ErrorAction SilentlyContinue
$headers.Clear()
$process=Start-Process -FilePath $setup -ArgumentList $arguments -WindowStyle Hidden -PassThru
try{if(-not $process.WaitForExit(90000)){$process.Kill($true);throw 'Downloaded installer timed out.'};if($process.ExitCode -ne 0){throw 'Downloaded installer failed.'}}finally{$process.Dispose()}
foreach($file in $manifest.payload){if((Get-FileHash -LiteralPath (Join-Path $installation $file.path)).Hash.ToLowerInvariant() -cne $file.sha256){throw 'Downloaded installer payload mismatch.'}}
$probe=Join-Path $sandbox 'probe publish'
& dotnet restore (Join-Path $root 'tests/Fixtures/GameProcessFixture/GameProcessFixture.csproj') --locked-mode
if($LASTEXITCODE -ne 0){throw 'Draft smoke fixture restore failed.'}
& dotnet publish (Join-Path $root 'tests/Fixtures/GameProcessFixture/GameProcessFixture.csproj') -c Release -r win-x64 --self-contained true --no-restore -p:Version=0.0.1 -o $probe
if($LASTEXITCODE -ne 0){throw 'Draft smoke fixture build failed.'}
foreach($name in @('GameProcessFixture.exe','GameProcessFixture.dll','GameProcessFixture.deps.json','GameProcessFixture.runtimeconfig.json')){Copy-Item -LiteralPath (Join-Path $probe $name) -Destination (Join-Path $installation $name)}
$request=[guid]::NewGuid().ToString('N')
$process=Start-Process -FilePath (Join-Path $installation 'GameProcessFixture.exe') -ArgumentList @('--installer-fixture','seed',$token,$request) -WorkingDirectory $installation -RedirectStandardError (Join-Path $evidence 'probe.stderr') -WindowStyle Hidden -PassThru
try{if(-not $process.WaitForExit(30000)){$process.Kill($true);throw 'Installed draft fixture timed out.'};if($process.ExitCode -ne 0){throw 'Installed draft library did not open.'}}finally{$process.Dispose()}
$fixture=Get-Content -LiteralPath (Join-Path $installation ('fixture-result-'+$request+'.json')) -Raw|ConvertFrom-Json
if($fixture.ok -ne $true -or [IO.Path]::TrimEndingDirectorySeparator($fixture.runtimeDirectory) -ine $installation -or
    $fixture.infrastructure -ine (Join-Path $installation 'GameVault.Infrastructure.dll') -or $fixture.summary.games -ne 2){throw 'Draft fixture did not use the installed runtime/library.'}
$fingerprint=Get-InstallerLibraryFingerprint $installation
if($fingerprint -cne $fixture.fingerprint){throw 'Draft fixture fingerprint disagrees.'}
$arguments='/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /LOG="'+(Join-Path $evidence 'uninstall.log')+'"'
$process=Start-Process -FilePath (Join-Path $installation 'unins000.exe') -ArgumentList $arguments -WindowStyle Hidden -PassThru
try{if(-not $process.WaitForExit(90000)){$process.Kill($true);throw 'Draft uninstall timed out.'};if($process.ExitCode -ne 0){throw 'Draft uninstall failed.'}}finally{$process.Dispose()}
Wait-InstallerLeaseRelease -Root $installation
if((Get-InstallerLibraryFingerprint $installation) -cne $fingerprint){throw 'Draft uninstall changed synthetic data.'}
foreach($file in $manifest.payload){if(Test-Path -LiteralPath (Join-Path $installation $file.path)){throw 'Draft uninstall retained owned files.'}}
$report=[ordered]@{schemaVersion=1;passed=$true;draftReleaseId=$ReleaseId;sourceCommit=$manifest.sourceCommit;buildRunId=$manifest.buildRunId;installerFile=$manifest.installerFile;
    downloadedInstallerSha256=$manifest.installerSha256;checksumVerified=$true;originArtifactId=$origin.artifactId;exactDownloadedAssetInstalled=$true;installedRuntimeUsed=$true;syntheticLibraryRead=$true;uninstallPreservedData=$true;
    os=[Environment]::OSVersion.VersionString;architecture=[Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString();interactiveUiValidated=$false;releasePublished=$false;releaseApproved=$false}
$report|ConvertTo-Json -Depth 4|Set-Content -LiteralPath (Join-Path $evidence 'report.json') -Encoding utf8
Write-Host ('Exact downloaded draft installer smoke passed: '+$manifest.installerSha256)
