# Real setup/uninstall execution is intentionally refused on developer PCs.
[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
Import-Module (Join-Path $PSScriptRoot 'InstallerLifecycle.psm1') -Force
Assert-DisposableInstallerRunner
$repository=Split-Path $PSScriptRoot -Parent
$verified=Get-Content -LiteralPath (Join-Path $repository 'artifacts/verification.json') -Raw | ConvertFrom-Json
$installerState=Join-Path $repository 'artifacts/installer.json'
$originalInstallerState=[IO.File]::ReadAllText($installerState)
$current=ConvertFrom-Json $originalInstallerState
$currentManifest=Get-Content -LiteralPath $current.manifest -Raw | ConvertFrom-Json
$token=[guid]::NewGuid()
$root=Join-Path ([IO.Path]::GetFullPath($env:RUNNER_TEMP)) ('GameVault installer lifecycle '+$token.ToString('N'))
$default=Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Programs/GameVault'
$custom=Join-Path $root 'Custom location with spaces/GameVault'
$programShortcut=Join-Path ([Environment]::GetFolderPath('Programs')) 'GameVault.lnk'
$desktopShortcut=Join-Path ([Environment]::GetFolderPath('DesktopDirectory')) 'GameVault.lnk'
$registration='HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{11DE0F05-D06D-4594-A970-85DDE91DC106}_is1'
foreach($existing in @($default,$programShortcut,$desktopShortcut,$registration)) {
    if(Test-Path -LiteralPath $existing){throw "Refusing a runner with a pre-existing GameVault install or shortcut: $existing"}
}
New-Item -ItemType Directory $root | Out-Null
$evidence=Join-Path $repository ('artifacts/installer-lifecycle-'+$token.ToString('N'))
New-Item -ItemType Directory $evidence | Out-Null
$observations=[Collections.Generic.List[object]]::new()
$activeFixture=$null
function Record([string]$Scenario,$Details) {
    $observations.Add([ordered]@{scenario=$Scenario;passed=$true;details=$Details})
    Write-Host "Installer lifecycle passed: $Scenario"
}
function Invoke-Build([string[]]$Arguments) {
    & dotnet @Arguments
    if($LASTEXITCODE -ne 0){throw "Fixture/payload build failed: $($Arguments[0])"}
}
function Assert-Owned([string]$Directory) {
    $full=[IO.Path]::GetFullPath($Directory)
    if($full -ine $default -and -not $full.StartsWith($root+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Installation target is outside the owned sandbox.'}
    if(-not(Test-Path -LiteralPath (Join-Path $full '.installer-fixture-token')) -or
        (Get-Content -LiteralPath (Join-Path $full '.installer-fixture-token') -Raw).Trim() -ne $token.ToString('N')){throw 'Missing installation ownership marker.'}
}
function New-Owned([string]$Directory) {
    if(Test-Path -LiteralPath $Directory){throw 'Existing installation target refused.'}
    New-Item -ItemType Directory $Directory -Force | Out-Null
    [IO.File]::WriteAllText((Join-Path $Directory '.installer-fixture-token'),$token.ToString('N'))
    Assert-Owned $Directory
}
function Invoke-Setup([string]$Executable,[string]$Directory,[string]$Scenario,[bool]$Success=$true,[bool]$Uninstall=$false,[string]$Tasks='__default__') {
    Assert-Owned $Directory
    $log=Join-Path $evidence ($Scenario+'.log')
    $arguments='/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /NOCLOSEAPPLICATIONS /NORESTARTAPPLICATIONS /LOG="'+$log+'"'
    if(-not $Uninstall){
        $arguments += ' /SP- /DIR="'+$Directory+'"'
        if($Tasks -ne '__default__'){$arguments += ' /TASKS="'+$Tasks+'"'}
    }
    $process=Start-Process -FilePath $Executable -ArgumentList $arguments -WindowStyle Hidden -PassThru
    try {
        if(-not $process.WaitForExit(90000)){$process.Kill($true);throw "Installer timed out: $Scenario"}
        if(($Success -and $process.ExitCode -ne 0) -or (-not $Success -and $process.ExitCode -eq 0)){throw "Unexpected installer exit $($process.ExitCode): $Scenario"}
        return $process.ExitCode
    } finally {$process.Dispose()}
}
function Copy-Probe([string]$Publish,[string]$Directory) {
    Assert-Owned $Directory
    foreach($name in @('GameProcessFixture.exe','GameProcessFixture.dll','GameProcessFixture.deps.json','GameProcessFixture.runtimeconfig.json')) {
        Copy-Item -LiteralPath (Join-Path $Publish $name) -Destination (Join-Path $Directory $name) -Force
    }
}
function Invoke-Probe([string]$Directory,[string]$Command) {
    Assert-Owned $Directory
    $request=[guid]::NewGuid().ToString('N')
    $output=Join-Path $Directory ('fixture-result-'+$request+'.json')
    $process=Start-Process -FilePath (Join-Path $Directory 'GameProcessFixture.exe') -ArgumentList @('--installer-fixture',$Command,$token.ToString('N'),$request) -WorkingDirectory $Directory -WindowStyle Hidden -RedirectStandardError (Join-Path $evidence ('probe-'+$request+'.stderr')) -PassThru
    $deadline=[DateTime]::UtcNow.AddSeconds(30)
    while(-not(Test-Path -LiteralPath $output)) {
        if($process.HasExited -or [DateTime]::UtcNow -gt $deadline){if(-not $process.HasExited){$process.Kill($true)};$process.Dispose();throw "Installed fixture did not report: $Command"}
        Start-Sleep -Milliseconds 100
    }
    $report=Get-Content -LiteralPath $output -Raw | ConvertFrom-Json
    if($Command -eq 'hold'){$script:activeFixture=[pscustomobject]@{process=$process;directory=$Directory;request=$request}}
    else {if(-not $process.WaitForExit(5000) -or $process.ExitCode -ne 0){throw 'Installed storage probe failed'};$process.Dispose()}
    if($report.ok -ne $true -or $report.summary.games -ne 2 -or $report.summary.pinned -ne 2 -or $report.summary.unpinned -ne 2 -or
        [IO.Path]::TrimEndingDirectorySeparator($report.runtimeDirectory) -ine $Directory -or
        $report.infrastructure -ine (Join-Path $Directory 'GameVault.Infrastructure.dll')){throw 'Probe did not use installed runtime and infrastructure.'}
    if($report.fingerprint -ne (Get-InstallerLibraryFingerprint $Directory)){throw 'Independent library fingerprint disagreed.'}
    return $report
}
function Stop-Probe {
    if($null -eq $script:activeFixture){return}
    $fixture=$script:activeFixture
    [IO.File]::WriteAllText((Join-Path $fixture.directory ('fixture-stop-'+$fixture.request)),'stop')
    if(-not $fixture.process.WaitForExit(10000)){$fixture.process.Kill($true);throw 'Owned fixture failed to stop'}
    $fixture.process.Dispose();$script:activeFixture=$null
}
function Assert-Preserved([string]$Directory,[string]$Fingerprint) {
    if((Get-InstallerLibraryFingerprint $Directory) -ne $Fingerprint){throw 'Retained library bytes, names or directories changed.'}
}
function Assert-Payload([string]$Directory,$Manifest) {
    foreach($file in $Manifest.payload) {
        $path=Join-Path $Directory $file.path
        if(-not(Test-Path -LiteralPath $path -PathType Leaf) -or (Get-FileHash -LiteralPath $path).Hash -ine $file.sha256){throw "Installed payload mismatch: $($file.path)"}
    }
}
function Assert-Registration([string]$Directory,[string]$Version) {
    $entry=Get-ItemProperty -LiteralPath $registration
    if($entry.DisplayVersion -ne $Version -or [IO.Path]::TrimEndingDirectorySeparator($entry.InstallLocation) -ine $Directory){throw 'Per-user install registration mismatch.'}
}
function Uninstall-Owned([string]$Directory,[string]$Scenario,[string]$Fingerprint) {
    $null=Invoke-Setup (Join-Path $Directory 'unins000.exe') $Directory $Scenario $true $true
    # Inno's first phase exits before the cleanup phase calls DeinitializeUninstall.
    Wait-InstallerLeaseRelease -Root $Directory
    Assert-Preserved $Directory $Fingerprint
    foreach($file in $currentManifest.payload){if(Test-Path -LiteralPath (Join-Path $Directory $file.path)){throw 'Uninstaller retained installer-owned payload.'}}
    if(Test-Path -LiteralPath $registration){throw 'Uninstall registration was retained.'}
    if((Test-Path -LiteralPath $programShortcut) -or (Test-Path -LiteralPath $desktopShortcut)){throw 'Uninstall retained a shortcut.'}
}
$passed=$false
try {
    $priorPublish=Join-Path $root 'prior publish'
    $probePublish=Join-Path $root 'probe publish'
    Invoke-Build @('publish',(Join-Path $repository 'src/GameVault.App/GameVault.App.csproj'),'-c','Release','-r','win-x64','--self-contained','true','--no-restore','-p:Version=0.0.1','-o',$priorPublish)
    & (Join-Path $PSScriptRoot 'copy-runtime-notices.ps1') -PublishDirectory $priorPublish
    & (Join-Path $PSScriptRoot 'copy-capture-prerequisites.ps1') -PublishDirectory $priorPublish
    & (Join-Path $PSScriptRoot 'build-installer.ps1') -PublishDirectory $priorPublish
    $prior=Get-Content -LiteralPath $installerState -Raw | ConvertFrom-Json
    $priorManifest=Get-Content -LiteralPath $prior.manifest -Raw | ConvertFrom-Json
    Invoke-Build @('publish',(Join-Path $repository 'tests/Fixtures/GameProcessFixture/GameProcessFixture.csproj'),'-c','Release','-r','win-x64','--self-contained','true','--no-restore','-p:Version=0.0.1','-o',$probePublish)
    New-Owned $default
    $null=Invoke-Setup $current.installer $default 'default-clean-install'
    Assert-Payload $default $currentManifest
    Assert-Registration $default $currentManifest.appVersion
    if(-not(Test-Path -LiteralPath $programShortcut) -or (Test-Path -LiteralPath $desktopShortcut)){throw 'Default shortcut choices were not respected.'}
    Copy-Probe $probePublish $default
    $initial=Invoke-Probe $default 'seed'
    Record 'default-clean-install-bundled-runtime' @{installedRuntime=$true;noExternalRuntimeUsed=$true;library=$initial.summary}
    Uninstall-Owned $default 'default-uninstall' $initial.fingerprint
    Record 'default-uninstall-preserves-library' @{fingerprint=$initial.fingerprint}
    New-Owned $custom
    $null=Invoke-Setup $prior.installer $custom 'prior-custom-install' $true $false 'startmenuicon,desktopicon'
    Assert-Payload $custom $priorManifest
    Assert-Registration $custom '0.0.1'
    if(-not(Test-Path -LiteralPath $programShortcut) -or -not(Test-Path -LiteralPath $desktopShortcut)){throw 'Requested shortcuts were not created.'}
    $shell=New-Object -ComObject WScript.Shell
    try {foreach($shortcut in @($programShortcut,$desktopShortcut)){$link=$shell.CreateShortcut($shortcut);try{if($link.TargetPath -ine (Join-Path $custom 'GameVault.exe') -or $link.WorkingDirectory -ine $custom){throw 'Shortcut target/working directory mismatch'}}finally{[Runtime.InteropServices.Marshal]::FinalReleaseComObject($link)|Out-Null}}}finally{[Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell)|Out-Null}
    Copy-Probe $probePublish $custom
    $seeded=Invoke-Probe $custom 'seed'
    $baseline=$seeded.fingerprint
    Record 'prior-version-custom-spaced-path-shortcuts' @{version='0.0.1';fingerprint=$baseline}
    $null=Invoke-Probe $custom 'hold'
    try {
        $blocked=Invoke-Setup $current.installer $custom 'running-app-upgrade-refused' $false
        if($blocked -ne 7){throw 'Upgrade did not stop in PrepareToInstall.'}
        $null=Invoke-Setup (Join-Path $custom 'unins000.exe') $custom 'running-app-uninstall-refused' $false $true
        Assert-Preserved $custom $baseline
        Assert-Payload $custom $priorManifest
        if($activeFixture.process.HasExited){throw 'Setup stopped the active fixture.'}
        Record 'running-app-upgrade-and-uninstall-refused' @{upgradeExit=$blocked;applicationWasNotKilled=$true}
    } finally {Stop-Probe}
    $fileLease=[IO.FileStream]::new((Join-Path $custom 'GameVault.Infrastructure.dll'),'Open','Read','Read')
    try {$failed=Invoke-Setup $current.installer $custom 'locked-file-upgrade-failed' $false} finally {$fileLease.Dispose()}
    Assert-Preserved $custom $baseline
    Record 'failed-upgrade-preserves-library' @{exitCode=$failed;failure='locked installed assembly';diskExhaustionSimulated=$false}
    $null=Invoke-Setup $current.installer $custom 'upgrade' $true $false 'startmenuicon,desktopicon'
    Assert-Payload $custom $currentManifest
    Assert-Registration $custom $currentManifest.appVersion
    Assert-Preserved $custom $baseline
    $null=Invoke-Probe $custom 'inspect'
    Record 'upgrade-preserves-and-reads-complete-library' @{from='0.0.1';to=$currentManifest.appVersion;fingerprint=$baseline}
    Uninstall-Owned $custom 'custom-uninstall' $baseline
    Record 'uninstall-preserves-data-and-removes-owned-files' @{fingerprint=$baseline}
    $null=Invoke-Setup $current.installer $custom 'reinstall' $true $false 'startmenuicon'
    Assert-Payload $custom $currentManifest
    Assert-Preserved $custom $baseline
    $null=Invoke-Probe $custom 'inspect'
    Record 'reinstall-reuses-retained-library' @{fingerprint=$baseline}
    $invalid=Join-Path $root 'Data/Rejected GameVault'
    New-Owned $invalid
    $invalidExit=Invoke-Setup $current.installer $invalid 'data-directory-target-refused' $false
    if($invalidExit -ne 7 -or (Test-Path -LiteralPath (Join-Path $invalid 'GameVault.exe'))){throw 'Data directory target was not safely refused.'}
    $actual=Join-Path $root 'Actual redirected target'
    New-Owned $actual
    [IO.File]::WriteAllText((Join-Path $actual 'untouched.bin'),'Synthetic redirection boundary')
    $redirect=Join-Path $root 'Redirected target'
    New-Item -ItemType Junction -Path $redirect -Target $actual | Out-Null
    $redirectExit=Invoke-Setup $current.installer $redirect 'redirected-target-refused' $false
    if($redirectExit -ne 7 -or (Test-Path -LiteralPath (Join-Path $actual 'GameVault.exe')) -or
        [IO.File]::ReadAllText((Join-Path $actual 'untouched.bin')) -ne 'Synthetic redirection boundary'){throw 'Redirected target was not safely refused.'}
    Assert-Preserved $custom $baseline
    Record 'invalid-and-redirected-targets-refused' @{invalidExit=$invalidExit;redirectExit=$redirectExit}
    Uninstall-Owned $custom 'final-uninstall' $baseline
    Assert-Preserved $default $initial.fingerprint
    $passed=$true
} finally {
    try {Stop-Probe} finally {
        [IO.File]::WriteAllText($installerState,$originalInstallerState)
        $report=[ordered]@{schemaVersion=1;automatedPassed=$passed;verificationRunId=$verified.runId;installerSha256=$current.sha256;os=[Environment]::OSVersion.VersionString;architecture=[Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString();runnerEnvironment=$env:RUNNER_ENVIRONMENT;syntheticPriorVersion=$true;observations=$observations.ToArray();interactiveUiValidated=$false;cleanMachineWithoutDevelopmentToolsValidated=$false;interactiveCancellation='pending-owner-final-pass';supportedWindowsMatrix='pending';releaseApproved=$false}
        $report|ConvertTo-Json -Depth 8|Set-Content -LiteralPath (Join-Path $evidence 'report.json') -Encoding utf8
        $report|ConvertTo-Json -Depth 8|Set-Content -LiteralPath (Join-Path $repository 'artifacts/installer-lifecycle.json') -Encoding utf8
        if(-not $passed){
            foreach($log in Get-ChildItem -LiteralPath $evidence -File | Where-Object Extension -in @('.log','.stderr')){
                Write-Host ('Failure evidence: '+$log.Name)
                Get-Content -LiteralPath $log.FullName -Tail 12 | Write-Host
            }
        }
    }
}
Write-Host "Installer lifecycle validation passed. Evidence: $evidence"
