[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$module=Join-Path $PSScriptRoot 'ReleasePackaging.psm1'
if(-not(Test-Path -LiteralPath $module)){throw 'Release packaging evidence validation has not been implemented.'}
Import-Module $module -Force
Import-Module (Join-Path $PSScriptRoot 'InstallerPayload.psm1') -Force
$root=Join-Path ([IO.Path]::GetTempPath()) ('GameVault release tests '+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory $root|Out-Null
$count=0
function Reject([scriptblock]$Action,[string]$Pattern){$message=$null;try{& $Action|Out-Null}catch{$message=$_.Exception.Message};if(-not $message -or $message -notmatch $Pattern){throw "Expected rejection '$Pattern', got '$message'"};$script:count++}
try {
    foreach($path in @('Profiles/id/statistics.json','Data/settings.json','outputs/build/GameVault.exe','saves/slot.sav','.env','certificate.pfx')){
        if(Test-ReleaseSourcePath $path){throw "Unsafe source path accepted: $path"};$count++
    }
    foreach($path in @('src/GameVault.App/App.xaml','tests/Fixtures/GameProcessFixture/Program.cs','src/GameVault.App/Assets/GameVault.ico')){
        if(-not(Test-ReleaseSourcePath $path)){throw "Source path rejected: $path"};$count++
    }
    if(-not(Test-ReleaseSecret ('ghp_'+('a'*36)))){throw 'Credential marker not detected'};$count++
    if(Test-ReleaseSecret 'Source fixture says synthetic saved progress'){throw 'Synthetic test content was treated as a secret'};$count++
    $publish=Join-Path $root 'publish';New-Item -ItemType Directory $publish|Out-Null
    foreach($name in @('GameVault.exe','GameVault.dll','GameVault.deps.json','coreclr.dll','hostfxr.dll','PresentationFramework.dll','THIRD-PARTY-NOTICES.txt','licenses/dotnet/LICENSE.txt','licenses/dotnet/THIRD-PARTY-NOTICES.txt','licenses/windowsdesktop/LICENSE.txt')){
        $path=Join-Path $publish $name;New-Item -ItemType Directory (Split-Path $path -Parent) -Force|Out-Null;[IO.File]::WriteAllText($path,'synthetic binary or notice')
    }
    [IO.File]::WriteAllText((Join-Path $publish 'GameVault.runtimeconfig.json'),'{"runtimeOptions":{"includedFrameworks":[{"name":"Microsoft.NETCore.App","version":"10.0.15"},{"name":"Microsoft.WindowsDesktop.App","version":"10.0.15"}]}}')
    $installer=Join-Path $root 'GameVault-Setup-0.1.0-win-x64.exe';[IO.File]::WriteAllText($installer,'synthetic installer')
    $hash=(Get-FileHash -LiteralPath $installer).Hash.ToLowerInvariant();$run=[guid]::NewGuid().ToString('N')
    $verification=[pscustomobject]@{runId=$run;publishDirectory=$publish}
    $validation=[pscustomobject]@{runId=$run;automatedWorkflowsPassed=$true}
    $lifecycle=[pscustomobject]@{schemaVersion=1;automatedPassed=$true;verificationRunId=$run;installerSha256=$hash}
    $manifest=[pscustomobject]@{schemaVersion=1;appVersion='0.1.0';publishDirectory=$publish;sha256=$hash;payload=@(Get-InstallerPayload $publish|ForEach-Object{[pscustomobject]@{path=[IO.Path]::GetRelativePath($publish,$_.FullName);sha256=(Get-FileHash -LiteralPath $_.FullName).Hash.ToLowerInvariant()}})}
    Assert-ReleaseEvidence $verification $validation $lifecycle $manifest $installer;$count++
    $lifecycle.verificationRunId=[guid]::NewGuid().ToString('N');Reject {Assert-ReleaseEvidence $verification $validation $lifecycle $manifest $installer} 'run';$lifecycle.verificationRunId=$run
    $lifecycle.installerSha256=('0'*64);Reject {Assert-ReleaseEvidence $verification $validation $lifecycle $manifest $installer} 'installer';$lifecycle.installerSha256=$hash
    $lifecycle.automatedPassed=$false;Reject {Assert-ReleaseEvidence $verification $validation $lifecycle $manifest $installer} 'lifecycle';$lifecycle.automatedPassed=$true
    [IO.File]::WriteAllText((Join-Path $publish 'GameVault.dll'),'changed');Reject {Assert-ReleaseEvidence $verification $validation $lifecycle $manifest $installer} 'payload'
    $bundle=Join-Path $root 'bundle';New-Item -ItemType Directory $bundle|Out-Null
    foreach($name in @('GameVault-Setup-0.1.0-win-x64.exe','GameVault-Setup-0.1.0-win-x64.exe.sha256','INSTALL.md','RELEASE-NOTES.md','THIRD-PARTY-NOTICES.txt','DOTNET-LICENSE.txt','DOTNET-THIRD-PARTY-NOTICES.txt')){[IO.File]::WriteAllText((Join-Path $bundle $name),'synthetic release asset')}
    $bundleManifest=@{schemaVersion=1;version='0.1.0';installerFile='GameVault-Setup-0.1.0-win-x64.exe';installerSha256=(Get-FileHash (Join-Path $bundle 'GameVault-Setup-0.1.0-win-x64.exe')).Hash.ToLowerInvariant();sourceCommit=('a'*40);headCommit=('a'*40);buildRunId='123';signed=$false;releaseApproved=$false;payload=@(@{path='GameVault.exe';sha256=('a'*64)})}
    $bundleManifest|ConvertTo-Json|Set-Content (Join-Path $bundle 'RELEASE-MANIFEST.json') -Encoding utf8
    function Write-Checksums {Get-ChildItem -LiteralPath $bundle -File|Where-Object Name -ne 'SHA256SUMS.txt'|Sort-Object Name|ForEach-Object{(Get-FileHash -LiteralPath $_.FullName).Hash.ToLowerInvariant()+'  '+$_.Name}|Set-Content -LiteralPath (Join-Path $bundle 'SHA256SUMS.txt') -Encoding ascii}
    $bundleManifest.installerSha256+'  '+$bundleManifest.installerFile|Set-Content (Join-Path $bundle 'GameVault-Setup-0.1.0-win-x64.exe.sha256') -Encoding ascii
    Write-Checksums;Assert-ReleaseBundle $bundle;$count++
    $origin=Join-Path $root 'trusted-ci-origin';New-Item -ItemType Directory $origin|Out-Null
    Get-ChildItem -LiteralPath $bundle -File|Copy-Item -Destination $origin
    Assert-ReleaseMatchesOrigin $bundle $origin;$count++
    [IO.File]::WriteAllText((Join-Path $bundle 'INSTALL.md'),'Changed but internally consistent candidate')
    Write-Checksums
    Reject {Assert-ReleaseMatchesOrigin $bundle $origin} 'originating CI artifact'
    [IO.File]::WriteAllText((Join-Path $bundle 'INSTALL.md'),'tampered');Reject {Assert-ReleaseBundle $bundle} 'checksum';Write-Checksums
    [IO.File]::WriteAllText((Join-Path $bundle 'personal.sav'),'synthetic forbidden save');Write-Checksums;Reject {Assert-ReleaseBundle $bundle} 'allowlist|unexpected'
    $packRoot=Join-Path $root 'runtime-packages'
    foreach($entry in @(@('microsoft.netcore.app.runtime.win-x64','LICENSE.TXT'),@('microsoft.netcore.app.runtime.win-x64','THIRD-PARTY-NOTICES.TXT'),@('microsoft.windowsdesktop.app.runtime.win-x64','LICENSE'))){
        $path=Join-Path $packRoot ($entry[0]+'/10.0.15/'+$entry[1]);New-Item -ItemType Directory (Split-Path $path -Parent) -Force|Out-Null;[IO.File]::WriteAllText($path,'Synthetic runtime notice '+$entry[0]+' '+$entry[1])
    }
    $fresh=Join-Path $root 'notice-publish';New-Item -ItemType Directory $fresh|Out-Null
    Copy-Item (Join-Path $publish 'GameVault.runtimeconfig.json') $fresh
    $assets=Join-Path $root 'project.assets.json';@{packageFolders=@{($packRoot) = @{}}}|ConvertTo-Json -Depth 3|Set-Content $assets -Encoding utf8
    & (Join-Path $PSScriptRoot 'copy-runtime-notices.ps1') -PublishDirectory $fresh -AssetsFile $assets
    if([IO.File]::ReadAllText((Join-Path $fresh 'licenses/dotnet/LICENSE.txt')) -cne [IO.File]::ReadAllText((Join-Path $packRoot 'microsoft.netcore.app.runtime.win-x64/10.0.15/LICENSE.TXT'))){throw 'Runtime license was not copied exactly'};$count++
    $missing=Join-Path $root 'missing-notice-publish';New-Item -ItemType Directory $missing|Out-Null;Copy-Item (Join-Path $publish 'GameVault.runtimeconfig.json') $missing
    Remove-Item -LiteralPath (Join-Path $packRoot 'microsoft.windowsdesktop.app.runtime.win-x64/10.0.15/LICENSE')
    Reject {& (Join-Path $PSScriptRoot 'copy-runtime-notices.ps1') -PublishDirectory $missing -AssetsFile $assets} 'runtime.*notice'
    Write-Host "Release packaging checks passed: $count"
} finally {
    $full=[IO.Path]::GetFullPath($root);$temporary=[IO.Path]::GetFullPath([IO.Path]::GetTempPath())
    if($full.StartsWith($temporary,[StringComparison]::OrdinalIgnoreCase) -and (Split-Path $full -Leaf) -like 'GameVault release tests *'){Remove-Item -LiteralPath $full -Recurse -Force}
}
