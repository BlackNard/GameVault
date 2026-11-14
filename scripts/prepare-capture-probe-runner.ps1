# Never provision multimedia components on an owner or self-hosted machine.
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Import-Module (Join-Path $PSScriptRoot 'InstallerLifecycle.psm1') -Force
Assert-DisposableInstallerRunner
Import-Module ServerManager -ErrorAction Stop
$feature = Get-WindowsFeature -Name Server-Media-Foundation
if ($null -eq $feature) { throw 'Runner does not expose the Media Foundation server feature.' }
if (-not $feature.Installed) {
    $result = Install-WindowsFeature -Name Server-Media-Foundation
    if (-not $result.Success -or $result.RestartNeeded -eq 'Yes') {
        throw 'Media Foundation could not be prepared without restarting the disposable runner.'
    }
}
if (-not (Get-WindowsFeature -Name Server-Media-Foundation).Installed) {
    throw 'Media Foundation is still unavailable.'
}
Write-Host 'Disposable runner Media Foundation is ready for synthetic image-only encoding.'
