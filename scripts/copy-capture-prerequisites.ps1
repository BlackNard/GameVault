[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$PublishDirectory,
    [string]$AssetsFile=(Join-Path (Split-Path $PSScriptRoot -Parent) 'src/GameVault.App/obj/project.assets.json'),
    [string]$ToolsDirectory=(Join-Path (Split-Path $PSScriptRoot -Parent) 'work/tools/capture-runtime')
)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
function Assert-SafePath([string]$Path) {
    $candidate=[IO.Path]::GetFullPath($Path)
    while($candidate){
        if(Test-Path -LiteralPath $candidate){
            $item=Get-Item -LiteralPath $candidate -Force
            if($item.Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Redirected capture prerequisite path refused.'}
        }
        $parent=Split-Path $candidate -Parent
        if($parent -eq $candidate){break}
        $candidate=$parent
    }
}
$root=[IO.Path]::GetFullPath($PublishDirectory)
Assert-SafePath $root
if(-not(Test-Path -LiteralPath $root -PathType Container)){throw 'Publish directory does not exist.'}
foreach($item in Get-ChildItem -LiteralPath $root -Recurse -Force){
    if(($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -or $item.Name -in @('Data','Profiles')){throw 'Personal or redirected publish data refused.'}
}
$assets=Get-Content -LiteralPath $AssetsFile -Raw|ConvertFrom-Json
if(-not $assets.libraries.PSObject.Properties['ScreenRecorderLib/7.0.1']){throw 'Exact restored ScreenRecorderLib 7.0.1 package is required.'}
$sources=@($assets.packageFolders.PSObject.Properties.Name|ForEach-Object{Join-Path $_ 'screenrecorderlib/7.0.1/LICENSE'}|Where-Object{Test-Path -LiteralPath $_ -PathType Leaf})
if($sources.Count -eq 0){throw 'Missing exact restored ScreenRecorderLib 7.0.1 LICENSE.'}
$license=$sources[0]
Assert-SafePath $license
$spec=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'capture-runtime.json') -Raw|ConvertFrom-Json
$cache=[IO.Path]::GetFullPath($ToolsDirectory)
Assert-SafePath $cache
New-Item -ItemType Directory -Path $cache -Force|Out-Null
$package=Join-Path $cache $spec.fileName
Assert-SafePath $package
if(-not(Test-Path -LiteralPath $package -PathType Leaf)){
    $download=Join-Path $cache ([guid]::NewGuid().ToString('N')+'.download')
    try {
        Invoke-WebRequest -Uri $spec.url -OutFile $download
        if((Get-FileHash -LiteralPath $download -Algorithm SHA256).Hash -ine $spec.sha256){throw 'Capture runtime SHA-256 mismatch.'}
        $signature=Get-AuthenticodeSignature -LiteralPath $download
        if($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch '(^|,\s*)O=Microsoft Corporation(,|$)'){throw 'Capture runtime requires a valid Microsoft Corporation Authenticode signature.'}
        Move-Item -LiteralPath $download -Destination $package
    } finally {if(Test-Path -LiteralPath $download){Remove-Item -LiteralPath $download}}
}
if((Get-FileHash -LiteralPath $package -Algorithm SHA256).Hash -ine $spec.sha256){throw 'Capture runtime SHA-256 mismatch.'}
$signature=Get-AuthenticodeSignature -LiteralPath $package
if($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch '(^|,\s*)O=Microsoft Corporation(,|$)'){throw 'Capture runtime requires a valid Microsoft Corporation Authenticode signature.'}
$copies=@(
    @{source=$package;destination=(Join-Path $root ('Prerequisites/'+$spec.fileName))},
    @{source=$license;destination=(Join-Path $root 'licenses/screenrecorderlib/LICENSE.txt')}
)
foreach($copy in $copies){
    Assert-SafePath $copy.destination
    if(Test-Path -LiteralPath $copy.destination){
        if((Get-FileHash -LiteralPath $copy.destination).Hash -ne (Get-FileHash -LiteralPath $copy.source).Hash){throw 'Existing capture prerequisite differs; refusing overwrite.'}
    }
}
foreach($copy in $copies){New-Item -ItemType Directory -Path (Split-Path $copy.destination -Parent) -Force|Out-Null;Copy-Item -LiteralPath $copy.source -Destination $copy.destination -Force}
Write-Host 'Pinned Microsoft capture runtime and exact ScreenRecorderLib license included; runtime was not executed.'
