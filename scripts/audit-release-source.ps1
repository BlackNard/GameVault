[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
Import-Module (Join-Path $PSScriptRoot 'ReleasePackaging.psm1') -Force
$root=Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
    $commit=(& git rev-parse HEAD).Trim();if($LASTEXITCODE -ne 0){throw 'Cannot identify source commit.'}
    $paths=@(& git -c core.quotepath=false log --all --name-only --pretty=format: --no-renames | Where-Object {$_}|Sort-Object -Unique)
    if($LASTEXITCODE -ne 0){throw 'Cannot enumerate reachable source history.'}
    $findings=[Collections.Generic.List[string]]::new()
    foreach($path in $paths){if(-not(Test-ReleaseSourcePath $path)){$findings.Add('Forbidden historical path: '+$path)}}
    $objects=@(& git -c core.quotepath=false rev-list --objects --all)
    if($LASTEXITCODE -ne 0){throw 'Cannot enumerate source blobs.'}
    $start=[Diagnostics.ProcessStartInfo]::new('git','cat-file --batch')
    $start.WorkingDirectory=$root;$start.UseShellExecute=$false;$start.CreateNoWindow=$true
    $start.RedirectStandardInput=$true;$start.RedirectStandardOutput=$true;$start.RedirectStandardError=$true
    $process=[Diagnostics.Process]::Start($start)
    $blobs=0;$binary=0
    try {
        foreach($object in $objects){
            $parts=$object -split ' ',2;$sha=$parts[0]
            $process.StandardInput.WriteLine($sha);$process.StandardInput.Flush()
            $header=[Collections.Generic.List[byte]]::new()
            while($true){$next=$process.StandardOutput.BaseStream.ReadByte();if($next -lt 0){throw 'Git blob stream ended unexpectedly.'};if($next -eq 10){break};$header.Add([byte]$next)}
            $description=[Text.Encoding]::ASCII.GetString($header.ToArray()) -split ' '
            if($description.Count -ne 3 -or $description[0] -cne $sha){throw 'Invalid Git blob header.'}
            $length=[int]$description[2]
            if($length -gt 8MB -or $length -lt 0){throw 'A large historical object requires a separate audit.'}
            $bytes=[byte[]]::new($length);$process.StandardOutput.BaseStream.ReadExactly($bytes)
            if($process.StandardOutput.BaseStream.ReadByte() -ne 10){throw 'Invalid Git blob boundary.'}
            if($description[1] -cne 'blob'){continue}
            $blobs++
            $path=if($parts.Count -eq 2){$parts[1]}else{''}
            if([IO.Path]::GetExtension($path) -in @('.ico','.png','.jpg','.jpeg')){$binary++;continue}
            if(Test-ReleaseSecret ([Text.Encoding]::UTF8.GetString($bytes))){$findings.Add('Recognizable credential marker in historical blob '+$sha+' ('+$path+')')}
        }
        $process.StandardInput.Close()
        if(-not $process.WaitForExit(10000) -or $process.ExitCode -ne 0){throw 'Git history reader failed.'}
    } finally {if(-not $process.HasExited){$process.Kill($true)};$process.Dispose()}
    $dirty=@(& git status --porcelain --untracked-files=no)
    if($LASTEXITCODE -ne 0){throw 'Cannot inspect source status.'}
    $report=[ordered]@{schemaVersion=1;passed=($findings.Count -eq 0);commitSha=$commit;workingTreeClean=($dirty.Count -eq 0);historicalPaths=$paths.Count;blobs=$blobs;binaryImagesSkippedForTextScan=$binary;coverage='reachable fetched Git history, forbidden paths and recognizable secret markers; not a universal secret detector';findings=$findings.ToArray()}
    $artifacts=Join-Path $root 'artifacts';New-Item -ItemType Directory $artifacts -Force|Out-Null
    $report|ConvertTo-Json -Depth 4|Set-Content -LiteralPath (Join-Path $artifacts 'source-audit.json') -Encoding utf8
    if($findings.Count -gt 0){$findings|ForEach-Object{Write-Host $_};throw 'Release source history audit failed.'}
    Write-Host "Reachable source history audit passed: $($paths.Count) paths, $blobs blobs."
} finally {Pop-Location}
