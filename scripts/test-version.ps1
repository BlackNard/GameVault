[CmdletBinding()]
param([string]$PublishDirectory)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = Split-Path $PSScriptRoot -Parent
Import-Module (Join-Path $PSScriptRoot 'AppVersion.psm1') -Force

# Checks that the app version is single-sourced and that the installer/release tooling agrees with it.
$version = Get-AppVersion $root
$problems = New-Object System.Collections.Generic.List[string]

foreach ($csproj in Get-ChildItem -LiteralPath (Join-Path $root 'src'), (Join-Path $root 'tests') -Filter *.csproj -Recurse) {
    if ((Get-Content -Raw -LiteralPath $csproj.FullName) -match '<(Version|AssemblyVersion|FileVersion|InformationalVersion|VersionPrefix)>') {
        $problems.Add("$($csproj.Name) sets its own version; only Directory.Build.props may.")
    }
}
$iss = Get-Content -Raw -LiteralPath (Join-Path $root 'installer/GameVault.iss')
if ($iss -match '(?m)^\s*#define\s+AppVersion\b' -or $iss -notmatch '\{#AppVersion\}') {
    $problems.Add('installer/GameVault.iss must take AppVersion from the compiler define only.')
}
if ((Get-Content -Raw -LiteralPath (Join-Path $root 'scripts/build-installer.ps1')) -notmatch 'Get-AppVersion') {
    $problems.Add('scripts/build-installer.ps1 no longer checks the payload against Directory.Build.props.')
}
if ($PublishDirectory) {
    $exe = Join-Path $PublishDirectory 'GameVault.exe'
    $built = (Get-Item -LiteralPath $exe).VersionInfo.ProductVersion.Split('+')[0]
    $expected = $version
    if ($built -ne $expected) { $problems.Add("GameVault.exe reports $built but Directory.Build.props says $expected.") }
}
if ($problems.Count -gt 0) { throw ("Version single-source check failed:`n" + ($problems -join "`n")) }
Write-Host "App version $version is single-sourced and consistent."
