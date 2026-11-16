[CmdletBinding()]
param([Parameter(Mandatory)][string]$PublishDirectory,
      [string]$AssetsFile=(Join-Path (Split-Path $PSScriptRoot -Parent) 'src/GameVault.App/obj/project.assets.json'))
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$root=[IO.Path]::GetFullPath($PublishDirectory)
$ancestor=Get-Item -LiteralPath $root -Force
while($null -ne $ancestor){if($ancestor.Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Redirected notice destination refused.'};$ancestor=$ancestor.Parent}
foreach($item in Get-ChildItem -LiteralPath $root -Recurse -Force){
    if(($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -or $item.Name -in @('Data','Profiles')){throw 'Personal or redirected publish data refused.'}
}
$runtime=Get-Content -LiteralPath (Join-Path $root 'GameVault.runtimeconfig.json') -Raw|ConvertFrom-Json
$folders=@((Get-Content -LiteralPath $AssetsFile -Raw|ConvertFrom-Json).packageFolders.PSObject.Properties.Name)
$specs=@(
    @{framework='Microsoft.NETCore.App';package='microsoft.netcore.app.runtime.win-x64';source='LICENSE.TXT';destination='licenses/dotnet/LICENSE.txt'},
    @{framework='Microsoft.NETCore.App';package='microsoft.netcore.app.runtime.win-x64';source='THIRD-PARTY-NOTICES.TXT';destination='licenses/dotnet/THIRD-PARTY-NOTICES.txt'},
    @{framework='Microsoft.WindowsDesktop.App';package='microsoft.windowsdesktop.app.runtime.win-x64';source='LICENSE';destination='licenses/windowsdesktop/LICENSE.txt'})
$copies=@()
foreach($spec in $specs){
    $framework=@($runtime.runtimeOptions.includedFrameworks|Where-Object name -eq $spec.framework)
    if($framework.Count -ne 1 -or $framework[0].version -notmatch '^\d+\.\d+\.\d+$'){throw 'Runtime notice version is missing or invalid.'}
    $sources=@($folders|ForEach-Object{Join-Path $_ ($spec.package+'/'+$framework[0].version+'/'+$spec.source)}|Where-Object{Test-Path -LiteralPath $_ -PathType Leaf})
    if($sources.Count -eq 0){throw ('Missing exact restored runtime notice: '+$spec.package+'/'+$spec.source)}
    $source=Get-Item -LiteralPath $sources[0] -Force
    if($source.Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Linked runtime notice source refused.'}
    $destination=Join-Path $root $spec.destination
    if(Test-Path -LiteralPath $destination){if((Get-FileHash -LiteralPath $destination).Hash -ne (Get-FileHash -LiteralPath $source.FullName).Hash){throw 'Existing runtime notice differs; refusing overwrite.'}}
    $copies+=@{source=$source.FullName;destination=$destination}
}
if(Test-Path -LiteralPath (Join-Path $root 'Microsoft.Web.WebView2.Core.dll')){
    $assets=Get-Content -LiteralPath $AssetsFile -Raw|ConvertFrom-Json
    $packages=@($assets.libraries.PSObject.Properties.Name|Where-Object{$_ -match '^Microsoft[.]Web[.]WebView2/[0-9]+[.][0-9]+[.][0-9]+[.][0-9]+$'})
    if($packages.Count -ne 1){throw 'WebView2 exact restored SDK notice version is missing.'}
    foreach($name in @('LICENSE.txt','NOTICE.txt')){
        $sources=@($folders|ForEach-Object{Join-Path $_ ($packages[0].ToLowerInvariant()+'/'+$name)}|Where-Object{Test-Path -LiteralPath $_ -PathType Leaf})
        if($sources.Count -eq 0){throw ('WebView2 exact restored SDK notice is missing: '+$name)}
        $source=Get-Item -LiteralPath $sources[0] -Force
        if($source.Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Linked WebView2 SDK notice source refused.'}
        $destination=Join-Path $root ('licenses/webview2/'+$name)
        if((Test-Path -LiteralPath $destination) -and (Get-FileHash -LiteralPath $destination).Hash -ne (Get-FileHash -LiteralPath $source.FullName).Hash){throw 'Existing WebView2 SDK notice differs; refusing overwrite.'}
        $copies+=@{source=$source.FullName;destination=$destination}
    }
}
if(Test-Path -LiteralPath (Join-Path $root 'Microsoft.Windows.SDK.NET.dll')){
    $source=Join-Path (Split-Path $PSScriptRoot -Parent) 'licenses/windows-sdk/LICENSE.rtf'
    if((Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash -ne 'DD07EB178E00C6BBA4148457FC00FF77CD4887EB521D504186FE59C9EC8BBE62'){throw 'Windows SDK publisher license hash mismatch.'}
    $copies+=@{source=$source;destination=(Join-Path $root 'licenses/windows-sdk/LICENSE.rtf')}
}
foreach($copy in $copies){New-Item -ItemType Directory -Path (Split-Path $copy.destination -Parent) -Force|Out-Null;Copy-Item -LiteralPath $copy.source -Destination $copy.destination -Force}
Write-Host 'Exact restored runtime licenses/notices included.'
