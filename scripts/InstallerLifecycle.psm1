Set-StrictMode -Version Latest
function Assert-DisposableInstallerRunner {
    if (-not $IsWindows -or $env:GITHUB_ACTIONS -cne 'true' -or $env:RUNNER_ENVIRONMENT -cne 'github-hosted' -or
        $env:RUNNER_OS -cne 'Windows' -or [string]::IsNullOrWhiteSpace($env:RUNNER_TEMP) -or
        -not [IO.Path]::IsPathFullyQualified($env:RUNNER_TEMP)) {
        throw 'Installer lifecycle execution is restricted to disposable GitHub-hosted Windows runners. Local and self-hosted execution is refused.'
    }
}
function Get-InstallerLibraryFingerprint {
    param([Parameter(Mandatory)][string]$Root)
    $lines = [Collections.Generic.List[string]]::new()
    foreach ($name in @('Data','Profiles')) {
        $directory = Join-Path $Root $name
        if (-not (Test-Path -LiteralPath $directory -PathType Container)) { throw 'Missing retained library directory.' }
        $pending = [Collections.Generic.Stack[string]]::new()
        $pending.Push($directory)
        while ($pending.Count -gt 0) {
            $folder = Get-Item -LiteralPath $pending.Pop() -Force
            if ($folder.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Linked library directories are refused.' }
            $lines.Add('D|' + [IO.Path]::GetRelativePath($Root,$folder.FullName))
            foreach ($item in Get-ChildItem -LiteralPath $folder.FullName -Force) {
                if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Linked library content is refused.' }
                if ($item.PSIsContainer) { $pending.Push($item.FullName) }
                else { $lines.Add('F|' + [IO.Path]::GetRelativePath($Root,$item.FullName) + '|' + (Get-FileHash -LiteralPath $item.FullName -Algorithm SHA256).Hash) }
            }
        }
    }
    $sorted = $lines.ToArray()
    [Array]::Sort($sorted,[StringComparer]::Ordinal)
    return [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes(($sorted -join "`n"))))
}
Export-ModuleMember -Function Assert-DisposableInstallerRunner,Get-InstallerLibraryFingerprint
