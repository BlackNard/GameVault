Set-StrictMode -Version Latest
function Get-InstallerPayload {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$PublishDirectory)
    $root = [IO.Path]::GetFullPath($PublishDirectory)
    if (-not (Test-Path -LiteralPath $root -PathType Container)) { throw 'Publish directory does not exist.' }
    $ancestor = Get-Item -LiteralPath $root -Force
    while ($null -ne $ancestor) {
        if ($ancestor.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Publish path contains a reparse point.' }
        $ancestor = $ancestor.Parent
    }
    $files = [Collections.Generic.List[object]]::new()
    $pending = [Collections.Generic.Stack[string]]::new()
    $pending.Push($root)
    while ($pending.Count -gt 0) {
        foreach ($item in Get-ChildItem -LiteralPath $pending.Pop() -Force) {
            if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Payload contains a reparse point: $($item.Name)" }
            if ($item.Name -match '^(Data|Profiles|Backups)$|^GameProcessFixture|^\.gamevault|^unins' -or
                $item.Extension -in @('.sav','.pfx','.p12','.key') -or $item.Name -like '.env*') {
                throw "Payload contains a forbidden or reserved name: $($item.Name)"
            }
            if ($item.Name -match '[{}";\r\n]') { throw "Payload contains an unsafe name: $($item.Name)" }
            if ($item.PSIsContainer) { $pending.Push($item.FullName) }
            else { $files.Add($item) }
        }
    }
    foreach ($name in @('GameVault.exe','GameVault.dll','GameVault.deps.json','GameVault.runtimeconfig.json',
        'coreclr.dll','hostfxr.dll','PresentationFramework.dll','THIRD-PARTY-NOTICES.txt',
        'licenses/dotnet/LICENSE.txt','licenses/dotnet/THIRD-PARTY-NOTICES.txt','licenses/windowsdesktop/LICENSE.txt')) {
        if (-not (Test-Path -LiteralPath (Join-Path $root $name) -PathType Leaf)) { throw "Payload is missing $name." }
    }
    $runtime = Get-Content -Raw -LiteralPath (Join-Path $root 'GameVault.runtimeconfig.json') | ConvertFrom-Json
    if (-not $runtime.runtimeOptions.PSObject.Properties['includedFrameworks']) { throw 'Payload must be self-contained.' }
    $frameworks = @($runtime.runtimeOptions.includedFrameworks | ForEach-Object { $_.name })
    if ('Microsoft.NETCore.App' -notin $frameworks -or 'Microsoft.WindowsDesktop.App' -notin $frameworks) {
        throw 'Payload must include both self-contained runtime frameworks.'
    }
    return $files | Sort-Object FullName
}
function Get-InstallerFileEntries {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$PublishDirectory)
    $root = [IO.Path]::GetFullPath($PublishDirectory).TrimEnd('\','/')
    foreach ($file in Get-InstallerPayload $root) {
        $relative = $file.FullName.Substring($root.Length + 1)
        $parent = Split-Path $relative -Parent
        $destination = if ($parent) { '{app}\' + $parent } else { '{app}' }
        'Source: "{#PayloadDirectory}\' + $relative + '"; DestDir: "' + $destination + '"; Flags: ignoreversion'
    }
}
Export-ModuleMember -Function Get-InstallerPayload,Get-InstallerFileEntries
