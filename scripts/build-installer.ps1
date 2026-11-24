[CmdletBinding()]
param([Parameter(Mandatory)][string]$PublishDirectory, [switch]$AllowDifferentVersion)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = Split-Path $PSScriptRoot -Parent
Import-Module (Join-Path $PSScriptRoot 'InstallerPayload.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'AppVersion.psm1') -Force
$payload = [IO.Path]::GetFullPath($PublishDirectory).TrimEnd('\','/')
if ($payload -match '[{}";\r\n]') { throw 'Unsafe publish path.' }
$files = @(Get-InstallerPayload $payload)
$version = (Get-Item -LiteralPath (Join-Path $payload 'GameVault.exe')).VersionInfo.ProductVersion.Split('+')[0].Split('-')[0]   # 0.1.0-preview.2 -> 0.1.0: the installer version has no preview part
if ($version -notmatch '^\d+\.\d+\.\d+(\.\d+)?$') { throw 'App version must be a numeric Windows installer version.' }
if (-not $AllowDifferentVersion -and $version -ne (Get-AppVersion $root)) { throw 'The publish directory was built from a different app version than Directory.Build.props.' }
$output = Join-Path $root ('artifacts/installer-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory $output | Out-Null
$entries = Join-Path $output 'payload.iss'
Get-InstallerFileEntries $payload | Set-Content -LiteralPath $entries -Encoding utf8
$guards = Join-Path $output 'destinations.iss'
$guardLines = @('function PayloadPathsAreSafe: Boolean;', 'begin', '  Result := False;')
foreach ($file in $files) {
    $relative = $file.FullName.Substring($payload.Length + 1).Replace("'", "''")
    $guardLines += "  if not PathIsSafe(ExpandConstant('{app}') + '\$relative') then Exit;"
}
$guardLines += @('  Result := True;', 'end;')
$guardLines | Set-Content -LiteralPath $guards -Encoding utf8
$compiler = & (Join-Path $PSScriptRoot 'get-installer-compiler.ps1')
& $compiler --quiet --no-ide-signtools --no-signing ('--define=PayloadDirectory='+$payload) ('--define=PayloadEntries='+$entries) ('--define=DestinationGuards='+$guards) `
    ('--define=SetupOutput='+$output) ('--define=AppVersion='+$version) (Join-Path $root 'installer/GameVault.iss')
if ($LASTEXITCODE -ne 0) { throw "Installer compilation failed: $LASTEXITCODE" }
$installer = Join-Path $output "GameVault-Setup-$version-win-x64.exe"
if (-not (Test-Path -LiteralPath $installer -PathType Leaf)) { throw 'Compiler did not produce the expected installer.' }
$hash = (Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $([IO.Path]::GetFileName($installer))" | Set-Content -LiteralPath ($installer+'.sha256') -Encoding ascii
$manifest = [ordered]@{
    schemaVersion = 1; appVersion = $version; architecture = 'win-x64'; signed = $false
    compilerVersion = '7.1.1'; compilerPackageSha256 = '8a5a6711c3766a6e5fb74f280ba0e3551e719ce94538d901fdbd1f8f9920b3f0'
    publishDirectory = $payload; installer = $installer; sha256 = $hash
    payload = @($files | ForEach-Object { [ordered]@{ path=$_.FullName.Substring($payload.Length+1); sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() } })
    ownerInstallerValidation = 'pending-final-pass'; releaseApproved = $false
}
$manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $output 'manifest.json') -Encoding utf8
[ordered]@{installer=$installer; manifest=(Join-Path $output 'manifest.json'); sha256=$hash} |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $root 'artifacts/installer.json') -Encoding utf8
Write-Host "Development installer compiled: $installer"
