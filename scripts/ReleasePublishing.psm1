Set-StrictMode -Version Latest

# The pure parts of publishing (issue #107), kept apart so they are tested without touching GitHub.

# The release's name: "GameVault <version>" unless another is asked for. It never says "Preview" or "Update" on its own (the owner's choice).
function Get-ReleaseTitle([string]$Version, [string]$Requested) {
    $title = if ([string]::IsNullOrWhiteSpace($Requested)) { "GameVault $Version" } else { $Requested.Trim() }
    if ($title -match 'preview') { throw 'A release name must not contain the word "Preview".' }
    if ($title.Length -gt 100 -or $title -match '[\r\n]') { throw 'The release name must be one line of at most 100 characters.' }
    return $title
}

# A release must not carry a lower number than one already published, or installed copies would never be offered it. (Earlier releases were tagged
# v<version>-preview.<N>; the same number without that ending is newer than they are.)
function Assert-NewerVersion([object[]]$Releases, [string]$Version) {
    foreach ($release in $Releases) {
        if ($null -eq $release -or $release.draft -or $release.tag_name -notmatch '^v(\d+(\.\d+){2,3})(-.+)?$') { continue }
        $published = [version]$Matches[1]
        $ending = $Matches[3]
        if ([version]$Version -lt $published -or ([version]$Version -eq $published -and -not $ending)) {
            throw "Version $Version is not newer than the published $($release.tag_name). Raise GameVaultVersion in Directory.Build.props."
        }
    }
}

# Reads one version's notes out of src/GameVault.Core/ReleaseNotes.cs: new("<version>", [ new lines ], [ fixed lines ]).
function Get-ReleaseNoteLines([string]$Source, [string]$Version) {
    $start = $Source.IndexOf('new("' + $Version + '",', [StringComparison]::Ordinal)
    if ($start -lt 0) { throw "ReleaseNotes.cs has no notes for $Version. Add them before publishing." }
    $lists = @([Collections.Generic.List[string]]::new(), [Collections.Generic.List[string]]::new())
    $at = $start + ('new("' + $Version + '",').Length
    $list = -1
    $open = $false
    while ($at -lt $Source.Length) {
        $c = $Source[$at]
        if ($c -eq '[') { $list++; $open = $true }
        elseif ($c -eq ']') { $open = $false; if ($list -ge 1) { break } }
        elseif ($c -eq '"' -and $open) {
            $text = [Text.StringBuilder]::new()
            for ($at++; $at -lt $Source.Length -and $Source[$at] -ne '"'; $at++) {
                if ($Source[$at] -eq '\') { $at++ }
                [void]$text.Append($Source[$at])
            }
            $lists[$list].Add($text.ToString())
        }
        $at++
    }
    if ($list -lt 1 -or ($lists[0].Count + $lists[1].Count) -eq 0) { throw "The notes for $Version could not be read." }
    return [pscustomobject]@{ New = @($lists[0]); Fixed = @($lists[1]) }
}

# The text of the release page, written for players.
function Get-ReleaseBody($Notes, [string]$Version, [string]$Evidence) {
    $lines = [Collections.Generic.List[string]]::new()
    foreach ($part in @(@{ Title = "What's new"; Items = $Notes.New }, @{ Title = 'Fixed'; Items = $Notes.Fixed })) {
        if (@($part.Items).Count -eq 0) { continue }
        $lines.Add('## ' + $part.Title)
        foreach ($item in $part.Items) { $lines.Add('- ' + $item) }
        $lines.Add('')
    }
    $lines.Add('## How to update')
    $lines.Add("An installed GameVault updates itself: it downloads this release and installs it when you close the app. Otherwise download ``GameVault-Setup-$Version-win-x64.exe`` below and run it over your installation. Your games, saves, backups, notes and settings stay as they are.")
    $lines.Add('')
    $lines.Add('The installer is unsigned, so Windows SmartScreen may warn about an unknown publisher.')
    $lines.Add('')
    $lines.Add('Build evidence: ' + $Evidence)
    return $lines -join "`n"
}

# The run a dispatch has just started: one that was not there before, for the wanted commit (any commit when none is given), the oldest first.
function Select-DispatchedRun([object[]]$Runs, [long[]]$Known, [string]$Commit) {
    return @($Runs | Where-Object { $null -ne $_ -and [long]$_.id -notin $Known -and (-not $Commit -or $_.head_sha -ceq $Commit) } | Sort-Object { [long]$_.id }) | Select-Object -First 1
}

Export-ModuleMember -Function Get-ReleaseTitle, Assert-NewerVersion, Get-ReleaseNoteLines, Get-ReleaseBody, Select-DispatchedRun
