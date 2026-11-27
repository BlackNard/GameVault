Set-StrictMode -Version Latest
Import-Module (Join-Path $PSScriptRoot 'InstallerPayload.psm1')
function Test-ReleaseSourcePath([string]$Path) {
    $pathName=$Path.Replace('\','/')
    return $pathName -notmatch '(^|/)(Data|Profiles|Backups|GameVault-TestData|outputs|work|bin|obj)(/|$)|(^|/)(\.env[^/]*|AGENTS\.md|Manage-TestData\.ps1|\.gamevault[^/]*)(/|$)|\.(exe|dll|pdb|sav|msi|msix|zip|pfx|p12|key|pem)$'
}
function Test-ReleaseSecret([string]$Content) {
    return $Content -cmatch '(gh[pousr]_[A-Za-z0-9_]{30,255}|github_pat_[A-Za-z0-9_]{40,255}|AKIA[A-Z0-9]{16}|-----BEGIN (RSA |EC |OPENSSH )?PRIVATE KEY-----)'
}
function Assert-ReleaseEvidence($Verification,$Validation,$Lifecycle,$Manifest,[string]$Installer) {
    if($Verification.runId -notmatch '^[0-9a-f]{32}$' -or $Validation.runId -ne $Verification.runId -or
        $Lifecycle.verificationRunId -ne $Verification.runId){throw 'Release evidence run IDs do not match.'}
    if($Validation.automatedWorkflowsPassed -ne $true -or $Lifecycle.schemaVersion -ne 1 -or $Lifecycle.automatedPassed -ne $true){throw 'Passing app and installer lifecycle evidence is required.'}
    if($Manifest.schemaVersion -ne 1 -or $Manifest.appVersion -notmatch '^\d+\.\d+\.\d+(\.\d+)?$' -or
        [IO.Path]::GetFileName($Installer) -cne ('GameVault-Setup-'+$Manifest.appVersion+'-win-x64.exe')){throw 'Invalid versioned installer identity.'}
    $hash=(Get-FileHash -LiteralPath $Installer -Algorithm SHA256).Hash.ToLowerInvariant()
    if($hash -cne $Manifest.sha256 -or $hash -cne $Lifecycle.installerSha256){throw 'Installer checksum is not bound to passing lifecycle evidence.'}
    if([IO.Path]::GetFullPath($Manifest.publishDirectory) -ine [IO.Path]::GetFullPath($Verification.publishDirectory)){throw 'Installer and verified payload directories differ.'}
    $files=@(Get-InstallerPayload $Verification.publishDirectory)
    if($files.Count -ne @($Manifest.payload).Count){throw 'Installer payload count changed after compilation.'}
    $expected=@{}
    foreach($entry in $Manifest.payload){
        if($expected.ContainsKey($entry.path)){throw 'Duplicate installer payload path.'}
        $expected[$entry.path]=$entry.sha256
    }
    foreach($file in $files){
        $relative=[IO.Path]::GetRelativePath($Verification.publishDirectory,$file.FullName)
        if(-not $expected.ContainsKey($relative) -or (Get-FileHash -LiteralPath $file.FullName).Hash -ine $expected[$relative]){throw 'Installer payload differs from the verified clean publish.'}
    }
}
function Get-ReleaseAssetNames([string]$Version) {
    if($Version -notmatch '^\d+\.\d+\.\d+(\.\d+)?$'){throw 'Invalid release version.'}
    $name='GameVault-Setup-'+$Version+'-win-x64.exe'
    return @($name,($name+'.sha256'),'INSTALL.md','RELEASE-NOTES.md','THIRD-PARTY-NOTICES.txt','DOTNET-LICENSE.txt','DOTNET-THIRD-PARTY-NOTICES.txt','RELEASE-MANIFEST.json','SHA256SUMS.txt')
}
function Assert-ReleaseBundle([string]$Directory) {
    $root=[IO.Path]::GetFullPath($Directory)
    if((Get-Item -LiteralPath $root -Force).Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Linked release bundle is refused.'}
    $manifest=Get-Content -LiteralPath (Join-Path $root 'RELEASE-MANIFEST.json') -Raw|ConvertFrom-Json
    if($manifest.schemaVersion -ne 1 -or $manifest.version -notmatch '^\d+\.\d+\.\d+(\.\d+)?$' -or
        $manifest.sourceCommit -notmatch '^[0-9a-f]{40}$' -or $manifest.headCommit -notmatch '^[0-9a-f]{40}$' -or
        $manifest.buildRunId -notmatch '^\d+$' -or $manifest.signed -ne $false -or $manifest.releaseApproved -ne $false){throw 'Invalid private development release manifest.'}
    if(@($manifest.payload).Count -lt 1 -or @($manifest.payload).Count -gt 2000){throw 'Invalid release payload catalog.'}
    $payloadPaths=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach($file in $manifest.payload){
        if([string]::IsNullOrWhiteSpace($file.path) -or [IO.Path]::IsPathRooted($file.path) -or
            $file.path -match '[:{}"\r\n]|(^|[\\/])(Data|Profiles|Backups|\.\.?|\.gamevault[^\\/]*)([\\/]|$)' -or
            $file.sha256 -notmatch '^[a-f0-9]{64}$' -or -not $payloadPaths.Add($file.path)){throw 'Unsafe or duplicate release payload catalog entry.'}
    }
    $name='GameVault-Setup-'+$manifest.version+'-win-x64.exe'
    if($manifest.installerFile -cne $name){throw 'Unexpected installer filename.'}
    $allowed=@(Get-ReleaseAssetNames $manifest.version)
    $items=@(Get-ChildItem -LiteralPath $root -Force)
    if($items.Count -ne $allowed.Count){throw 'Release bundle contains unexpected or missing files outside the allowlist.'}
    foreach($item in $items){
        if($item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -or $item.Name -cnotin $allowed){throw 'Release bundle contains an unexpected or unsafe file outside the allowlist.'}
        if($item.Name -cne $name -and (Test-ReleaseSecret ([IO.File]::ReadAllText($item.FullName)))){throw 'Release text contains a recognizable credential marker.'}
    }
    $seen=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach($line in Get-Content -LiteralPath (Join-Path $root 'SHA256SUMS.txt')){
        if($line -notmatch '^([a-f0-9]{64})  ([A-Za-z0-9._-]+)$'){throw 'Invalid release checksum line.'}
        $hash=$Matches[1];$file=$Matches[2]
        if($file -ceq 'SHA256SUMS.txt' -or $file -cnotin $allowed -or -not $seen.Add($file)){throw 'Unexpected or duplicate release checksum file.'}
        if((Get-FileHash -LiteralPath (Join-Path $root $file)).Hash.ToLowerInvariant() -cne $hash){throw "Release checksum mismatch: $file"}
    }
    if($seen.Count -ne $allowed.Count-1){throw 'Incomplete release checksum list.'}
    $installerHash=(Get-FileHash -LiteralPath (Join-Path $root $name)).Hash.ToLowerInvariant()
    if($installerHash -cne $manifest.installerSha256 -or
        [IO.File]::ReadAllText((Join-Path $root ($name+'.sha256'))).Trim() -cne ($installerHash+'  '+$name)){throw 'Installer checksum file/manifest mismatch.'}
}
function Assert-ReleaseMatchesOrigin([string]$Candidate,[string]$Origin) {
    Assert-ReleaseBundle $Candidate
    Assert-ReleaseBundle $Origin
    $manifest=Get-Content -LiteralPath (Join-Path $Origin 'RELEASE-MANIFEST.json') -Raw|ConvertFrom-Json
    foreach($name in Get-ReleaseAssetNames $manifest.version){
        if((Get-FileHash -LiteralPath (Join-Path $Candidate $name)).Hash -cne (Get-FileHash -LiteralPath (Join-Path $Origin $name)).Hash){throw 'Release bundle does not match the originating CI artifact.'}
    }
}
function Assert-ReleaseOrigin([string]$BundleDirectory,[string]$Api,[hashtable]$Headers) {
    Assert-ReleaseBundle $BundleDirectory
    if($Api -cne 'https://api.github.com/repos/BlackNard/GameVault'){throw 'Unexpected release origin API.'}
    $manifest=Get-Content -LiteralPath (Join-Path $BundleDirectory 'RELEASE-MANIFEST.json') -Raw|ConvertFrom-Json
    $run=Invoke-RestMethod ($Api+'/actions/runs/'+$manifest.buildRunId) -Headers $Headers
    if($run.status -cne 'completed' -or $run.conclusion -cne 'success' -or $run.head_sha -cne $manifest.headCommit -or
        $run.path -cne '.github/workflows/build.yml' -or $run.repository.full_name -cne 'BlackNard/GameVault'){throw 'Origin is not the successful expected CI workflow and commit.'}
    $catalog=Invoke-RestMethod ($Api+'/actions/runs/'+$manifest.buildRunId+'/artifacts?name=private-draft-assets&per_page=100') -Headers $Headers
    $matchingArtifacts=@($catalog.artifacts|Where-Object name -ceq 'private-draft-assets')
    if($matchingArtifacts.Count -ne 1 -or $matchingArtifacts[0].expired -ne $false -or $matchingArtifacts[0].size_in_bytes -gt 400MB){throw 'A unique unexpired originating CI artifact is required.'}
    $artifact=$matchingArtifacts[0]
    if($artifact.digest -notmatch '^sha256:([a-f0-9]{64})$'){throw 'Originating CI artifact has no trusted archive digest.'}
    $digest=$Matches[1]
    $directory=Join-Path (Split-Path $PSScriptRoot -Parent) ('artifacts/origin-auth-'+[guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory $directory -Force|Out-Null
    $archivePath=Join-Path $directory 'origin.zip'
    Invoke-WebRequest ($Api+'/actions/artifacts/'+$artifact.id+'/zip') -Headers $Headers -OutFile $archivePath
    if((Get-FileHash -LiteralPath $archivePath).Hash.ToLowerInvariant() -cne $digest){throw 'Originating CI artifact archive digest mismatch.'}
    $extracted=Join-Path $directory 'bundle';New-Item -ItemType Directory $extracted|Out-Null
    $allowed=@(Get-ReleaseAssetNames $manifest.version)
    $seen=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip=[IO.Compression.ZipFile]::OpenRead($archivePath)
    try {
        [long]$total=0
        foreach($entry in $zip.Entries){
            if($entry.Name.Length -eq 0){continue}
            $total+=$entry.Length
            if($entry.Name -cnotin $allowed -or -not $seen.Add($entry.Name) -or $entry.Length -gt 250MB -or $total -gt 400MB){throw 'Originating CI artifact has unexpected, duplicate or oversized assets.'}
            $input=$entry.Open();$output=[IO.File]::Open((Join-Path $extracted $entry.Name),'CreateNew','Write','None')
            try{$input.CopyTo($output)}finally{$input.Dispose();$output.Dispose()}
        }
    } finally {$zip.Dispose()}
    Assert-ReleaseMatchesOrigin $BundleDirectory $extracted
    return [pscustomobject]@{artifactId=$artifact.id;archiveSha256=$digest;buildRunId=$manifest.buildRunId}
}
function Assert-TagAvailable([object[]]$Releases,[object[]]$Refs,[string]$Tag) {
    foreach($release in $Releases){if($null -ne $release -and $release.tag_name -ceq $Tag){throw 'Existing release draft/tag will not be replaced. Raise the version in Directory.Build.props.'}}
    foreach($ref in $Refs){if($null -ne $ref -and $ref.ref -ceq ('refs/tags/'+$Tag)){throw 'Existing Git tag will not be reused or replaced.'}}
}
Export-ModuleMember -Function Test-ReleaseSourcePath,Test-ReleaseSecret,Assert-ReleaseEvidence,Assert-ReleaseBundle,Get-ReleaseAssetNames,Assert-ReleaseMatchesOrigin,Assert-ReleaseOrigin,Assert-TagAvailable
