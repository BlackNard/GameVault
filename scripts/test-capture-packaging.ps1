[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$root=Join-Path ([IO.Path]::GetTempPath()) ('GameVault capture packaging tests '+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory $root|Out-Null
$count=0
function Reject([scriptblock]$Action,[string]$Pattern){$message=$null;try{& $Action|Out-Null}catch{$message=$_.Exception.Message};if(-not $message -or $message -notmatch $Pattern){throw "Expected rejection '$Pattern', got '$message'"};$script:count++}
try {
    $publish=Join-Path $root 'publish';$cache=Join-Path $root 'cache';$packages=Join-Path $root 'packages'
    New-Item -ItemType Directory $publish,$cache,(Join-Path $packages 'screenrecorderlib/7.0.1')|Out-Null
    $license=Join-Path $packages 'screenrecorderlib/7.0.1/LICENSE'
    [IO.File]::WriteAllText($license,"Synthetic exact package license`r`n")
    [IO.File]::WriteAllText((Join-Path $cache 'VC_redist.x64.exe'),'synthetic runtime - never executed')
    $assets=Join-Path $root 'assets.json'
    @{libraries=@{'ScreenRecorderLib/7.0.1'=@{type='package'}};packageFolders=@{($packages)=@{}}}|ConvertTo-Json -Depth 4|Set-Content -LiteralPath $assets
    $copier=Join-Path $PSScriptRoot 'copy-capture-prerequisites.ps1'
    Reject {& $copier -PublishDirectory $publish -AssetsFile $assets -ToolsDirectory $cache} 'SHA-256 mismatch'
    # Mock only verification boundaries; fixture bytes can never execute as a program.
    $expected=(Get-Content (Join-Path $PSScriptRoot 'capture-runtime.json') -Raw|ConvertFrom-Json).sha256
    function Get-FileHash {param([string]$LiteralPath,[string]$Algorithm='SHA256');if([IO.Path]::GetFileName($LiteralPath) -eq 'VC_redist.x64.exe'){return [pscustomobject]@{Hash=$expected}};Microsoft.PowerShell.Utility\Get-FileHash -LiteralPath $LiteralPath -Algorithm $Algorithm}
    $signatureStatus='NotSigned';$subject='CN=Microsoft Corporation, O=Microsoft Corporation, C=US'
    function Get-AuthenticodeSignature {param([string]$LiteralPath);[pscustomobject]@{Status=$signatureStatus;SignerCertificate=[pscustomobject]@{Subject=$subject}}}
    function Invoke-WebRequest {throw 'Tests must never download or execute a runtime.'}
    Reject {& $copier -PublishDirectory $publish -AssetsFile $assets -ToolsDirectory $cache} 'Authenticode'
    $signatureStatus='Valid';$subject='CN=Other publisher, O=Other publisher, C=US'
    Reject {& $copier -PublishDirectory $publish -AssetsFile $assets -ToolsDirectory $cache} 'Microsoft Corporation'
    if(Test-Path (Join-Path $publish 'Prerequisites')){throw 'Rejected runtime was copied'};$count++
    $subject='CN=Microsoft Corporation, O=Microsoft Corporation, C=US'
    & $copier -PublishDirectory $publish -AssetsFile $assets -ToolsDirectory $cache
    if([IO.File]::ReadAllBytes($license).Length -ne [IO.File]::ReadAllBytes((Join-Path $publish 'licenses/screenrecorderlib/LICENSE.txt')).Length -or
       (Microsoft.PowerShell.Utility\Get-FileHash -LiteralPath $license).Hash -ne (Microsoft.PowerShell.Utility\Get-FileHash -LiteralPath (Join-Path $publish 'licenses/screenrecorderlib/LICENSE.txt')).Hash){throw 'License was not copied byte-for-byte'};$count++
    & $copier -PublishDirectory $publish -AssetsFile $assets -ToolsDirectory $cache
    $count++
    [IO.File]::WriteAllText((Join-Path $publish 'licenses/screenrecorderlib/LICENSE.txt'),'different')
    Reject {& $copier -PublishDirectory $publish -AssetsFile $assets -ToolsDirectory $cache} 'refusing overwrite'
    New-Item -ItemType Directory (Join-Path $publish 'Profiles')|Out-Null
    Reject {& $copier -PublishDirectory $publish -AssetsFile $assets -ToolsDirectory $cache} 'Personal'
    Remove-Item -LiteralPath (Join-Path $publish 'Profiles')
    $linked=Join-Path $root 'linked';New-Item -ItemType Junction -Path $linked -Target $publish|Out-Null
    try {Reject {& $copier -PublishDirectory $linked -AssetsFile $assets -ToolsDirectory $cache} 'Redirected'} finally {[IO.Directory]::Delete($linked)}
    $badAssets=Join-Path $root 'wrong-assets.json'
    @{libraries=@{'ScreenRecorderLib/7.0.0'=@{type='package'}};packageFolders=@{($packages)=@{}}}|ConvertTo-Json -Depth 4|Set-Content $badAssets
    Reject {& $copier -PublishDirectory $publish -AssetsFile $badAssets -ToolsDirectory $cache} 'Exact restored'
    Remove-Item -LiteralPath $license
    Reject {& $copier -PublishDirectory $publish -AssetsFile $assets -ToolsDirectory $cache} 'Missing exact'
    $wizard=Get-Content (Join-Path (Split-Path $PSScriptRoot -Parent) 'installer/GameVault.iss')
    $runtimeEntry=@($wizard|Where-Object{$_ -match '^Filename:.*VC_redist'})
    if($runtimeEntry.Count -ne 1 -or $runtimeEntry[0] -notmatch 'Verb: "runas"' -or $runtimeEntry[0] -notmatch 'postinstall skipifsilent unchecked' -or
        @($wizard|Where-Object{$_ -match 'Verb: "runas"'}).Count -ne 1 -or 'PrivilegesRequired=lowest' -notin $wizard){throw 'Runtime elevation must be optional and isolated'};$count++
    Write-Host "Capture prerequisite packaging checks passed: $count"
} finally {
    $full=[IO.Path]::GetFullPath($root);$temporary=[IO.Path]::GetFullPath([IO.Path]::GetTempPath())
    if($full.StartsWith($temporary,[StringComparison]::OrdinalIgnoreCase) -and (Split-Path $full -Leaf) -like 'GameVault capture packaging tests *'){Remove-Item -LiteralPath $full -Recurse -Force}
}
