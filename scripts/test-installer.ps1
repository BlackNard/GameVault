[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$module = Join-Path $PSScriptRoot 'InstallerPayload.psm1'
if (-not (Test-Path $module)) { throw 'Installer payload validator has not been implemented.' }
Import-Module $module -Force
$root = Join-Path ([IO.Path]::GetTempPath()) ('GameVault installer tests ' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory $root | Out-Null
$count = 0
function Assert-Rejected([scriptblock]$Action, [string]$Pattern) {
    $caught = $null
    try { & $Action | Out-Null } catch { $caught = $_.Exception.Message }
    if (-not $caught -or $caught -notmatch $Pattern) { throw "Expected rejection '$Pattern'; got '$caught'" }
    $script:count++
}
function New-Payload([string]$Name) {
    $dir = Join-Path $root $Name
    New-Item -ItemType Directory $dir | Out-Null
    foreach ($name in @('GameVault.exe','GameVault.dll','GameVault.Capture.dll','ScreenRecorderLib.dll','Prerequisites/VC_redist.x64.exe','licenses/screenrecorderlib/LICENSE.txt','GameVault.deps.json','coreclr.dll','hostfxr.dll','PresentationFramework.dll','THIRD-PARTY-NOTICES.txt','licenses/dotnet/LICENSE.txt','licenses/dotnet/THIRD-PARTY-NOTICES.txt','licenses/windowsdesktop/LICENSE.txt')) {
        $file=Join-Path $dir $name
        New-Item -ItemType Directory -Path (Split-Path $file -Parent) -Force | Out-Null
        [IO.File]::WriteAllText($file, 'synthetic payload')
    }
    [IO.File]::WriteAllText((Join-Path $dir 'GameVault.runtimeconfig.json'), '{"runtimeOptions":{"includedFrameworks":[{"name":"Microsoft.NETCore.App","version":"10.0.0"},{"name":"Microsoft.WindowsDesktop.App","version":"10.0.0"}]}}')
    return $dir
}
try {
    $good = New-Payload 'clean'
    $files = @(Get-InstallerPayload $good)
    if ($files.Count -ne 15) { throw 'Incomplete explicit file manifest' }; $count++
    New-Item -ItemType Directory (Join-Path $good 'fr') | Out-Null
    [IO.File]::WriteAllText((Join-Path $good 'fr/messages.dll'),'satellite')
    $entries = @(Get-InstallerFileEntries $good)
    if (-not ($entries -match 'DestDir: "\{app\}\\fr"')) { throw 'Nested file destination was lost' }; $count++
    $missing = New-Payload 'missing-runtime'
    Remove-Item -LiteralPath (Join-Path $missing 'coreclr.dll')
    Assert-Rejected { Get-InstallerPayload $missing } 'missing.*coreclr'
    foreach ($required in @('GameVault.Capture.dll','ScreenRecorderLib.dll','Prerequisites/VC_redist.x64.exe','licenses/screenrecorderlib/LICENSE.txt')) {
        $missingCapture = New-Payload ('missing-capture-'+[guid]::NewGuid().ToString('N'))
        Remove-Item -LiteralPath (Join-Path $missingCapture $required)
        Assert-Rejected { Get-InstallerPayload $missingCapture } ('missing '+[regex]::Escape($required))
    }
    $framework = New-Payload 'framework-dependent'
    [IO.File]::WriteAllText((Join-Path $framework 'GameVault.runtimeconfig.json'),'{"runtimeOptions":{}}')
    Assert-Rejected { Get-InstallerPayload $framework } 'self-contained'
    foreach ($name in @('Data','Profiles','nested/Profiles','GameProcessFixture.exe','.gamevault.lock','unins000.exe','bad{constant}.dll')) {
        $bad = New-Payload ('bad-'+[guid]::NewGuid().ToString('N'))
        $path = Join-Path $bad $name
        New-Item -ItemType Directory -Force (Split-Path $path -Parent) | Out-Null
        [IO.File]::WriteAllText($path,'private or unsafe')
        Assert-Rejected { Get-InstallerPayload $bad } 'forbidden|unsafe|reserved'
    }
    $linked = New-Payload 'linked'
    New-Item -ItemType Junction -Path (Join-Path $linked 'redirect') -Target $good | Out-Null
    Assert-Rejected { Get-InstallerPayload $linked } 'reparse'
    # Remove only the junction itself, never its target.
    [IO.Directory]::Delete((Join-Path $linked 'redirect'))
    # Exercise the same Win32 CreateFileW contract used by the Pascal wizard,
    # against the actual managed exclusive-file lease, without running setup/UI.
    if (-not ('GameVaultInstallerLease' -as [type])) {
        Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class GameVaultInstallerLease {
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
    public static extern IntPtr CreateFileW(string name, uint access, uint sharing,
        IntPtr security, uint creation, uint attributes, IntPtr template);
    [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr handle);
}
"@
    }
    $lockPath = Join-Path $root '.gamevault.lock'
    [IO.File]::WriteAllText($lockPath, 'coordination marker')
    $managed = [IO.File]::Open($lockPath, 'OpenOrCreate', 'ReadWrite', 'None')
    try {
        $handle = [GameVaultInstallerLease]::CreateFileW($lockPath, 3221225472, 0, [IntPtr]::Zero, 4, 128, [IntPtr]::Zero)
        if ($handle -ne [IntPtr]::new(-1)) {
            [GameVaultInstallerLease]::CloseHandle($handle) | Out-Null
            throw 'Installer lease did not refuse an active managed app lease.'
        }
        $count++
    } finally { $managed.Dispose() }
    $handle = [GameVaultInstallerLease]::CreateFileW($lockPath, 3221225472, 0, [IntPtr]::Zero, 4, 128, [IntPtr]::Zero)
    if ($handle -eq [IntPtr]::new(-1)) { throw 'Installer lease did not open after app quit.' }
    try {
        Assert-Rejected { $stream = [IO.File]::Open($lockPath,'OpenOrCreate','ReadWrite','None'); $stream.Dispose() } 'another process'
    } finally { [GameVaultInstallerLease]::CloseHandle($handle) | Out-Null }
    $managed = [IO.File]::Open($lockPath,'OpenOrCreate','ReadWrite','None')
    $managed.Dispose()
    if ([IO.File]::ReadAllText($lockPath) -ne 'coordination marker') { throw 'Lease acquisition modified file contents.' }
    $count++
    $lifecycleModule = Join-Path $PSScriptRoot 'InstallerLifecycle.psm1'
    if (-not (Test-Path -LiteralPath $lifecycleModule)) { throw 'Lifecycle safety guard has not been implemented.' }
    Import-Module $lifecycleModule -Force
    $previousRunner = $env:RUNNER_ENVIRONMENT
    try {
        $env:RUNNER_ENVIRONMENT = 'self-hosted'
        Assert-Rejected { Assert-DisposableInstallerRunner } 'GitHub-hosted Windows'
    } finally { $env:RUNNER_ENVIRONMENT = $previousRunner }
    $library = Join-Path $root 'hash-library'
    New-Item -ItemType Directory (Join-Path $library 'Data'),(Join-Path $library 'Profiles') | Out-Null
    [IO.File]::WriteAllText((Join-Path $library 'Data/settings.json'),'synthetic')
    $fingerprint = Get-InstallerLibraryFingerprint $library
    if ($fingerprint -ne (Get-InstallerLibraryFingerprint $library)) { throw 'Library fingerprint is unstable.' }; $count++
    [IO.File]::WriteAllText((Join-Path $library 'Data/settings.json'),'changed')
    if ($fingerprint -eq (Get-InstallerLibraryFingerprint $library)) { throw 'Library fingerprint missed a changed file.' }; $count++
    if (-not ('InstallerLeaseTestRelease' -as [type])) {
        Add-Type @"
public static class InstallerLeaseTestRelease {
    public static System.Threading.Tasks.Task ReleaseAfter(System.IO.FileStream stream, int milliseconds) {
        return System.Threading.Tasks.Task.Run(async () => { await System.Threading.Tasks.Task.Delay(milliseconds); stream.Dispose(); });
    }
}
"@
    }
    $held=[IO.File]::Open($lockPath,'Open','ReadWrite','None')
    try { Assert-Rejected { Wait-InstallerLeaseRelease -Root $root -TimeoutSeconds 0.1 } 'timed out' } finally { $held.Dispose() }
    $held=[IO.File]::Open($lockPath,'Open','ReadWrite','None')
    try {
        $release=[InstallerLeaseTestRelease]::ReleaseAfter($held,150)
        Wait-InstallerLeaseRelease -Root $root -TimeoutSeconds 3
        $null=$release.GetAwaiter().GetResult()
        if([IO.File]::ReadAllText($lockPath) -ne 'coordination marker'){throw 'Lease wait changed the coordination file'}
        $count++
    } finally {$held.Dispose()}
    Write-Host "Installer payload, lease and lifecycle-guard checks passed: $count"
}
finally {
    $full = [IO.Path]::GetFullPath($root)
    if ($full.StartsWith([IO.Path]::GetFullPath([IO.Path]::GetTempPath()), [StringComparison]::OrdinalIgnoreCase) -and
        (Split-Path $full -Leaf) -like 'GameVault installer tests *') {
        Remove-Item -LiteralPath $full -Recurse -Force
    }
}
