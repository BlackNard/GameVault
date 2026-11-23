#requires -Version 7
<#
Fails when a commit exposes a personal e-mail address. Every author and committer address, and every
Co-authored-by / Signed-off-by trailer, must be a GitHub no-reply address (or GitHub's own noreply@github.com,
or noreply@anthropic.com for the assistant's co-author line).

The offending address itself is never printed (CI logs of a public repository are public); only the
commit, the field and the fix are shown.

  ./scripts/audit-commit-emails.ps1 -Range 'origin/main..HEAD'     # the commits of a pull request
  ./scripts/audit-commit-emails.ps1                                 # the last 50 commits of HEAD
  ./scripts/audit-commit-emails.ps1 -Repository <folder>            # another repository (used by tests)
#>
param(
    [string]$Range = '',
    [string]$Repository = '.'
)
$ErrorActionPreference = 'Stop'

$allowed = @(
    '^[0-9]+\+[A-Za-z0-9](?:[A-Za-z0-9-]*[A-Za-z0-9])?(?:\[bot\])?@users\.noreply\.github\.com$',
    '^[A-Za-z0-9](?:[A-Za-z0-9-]*[A-Za-z0-9])?(?:\[bot\])?@users\.noreply\.github\.com$',
    '^noreply@github\.com$',
    '^noreply@anthropic\.com$'
)
function Test-Allowed([string]$email) {
    $value = $email.Trim()
    foreach ($pattern in $allowed) { if ($value -imatch $pattern) { return $true } }
    return $false
}

$repo = (Resolve-Path -LiteralPath $Repository).Path
$arguments = @('-C', $repo, 'log', '--format=%H%x1f%ae%x1f%ce%x1f%B%x1e')
if ([string]::IsNullOrWhiteSpace($Range) -or $Range -match '^0+\.\.') { $arguments += @('-n', '50', 'HEAD') } else { $arguments += $Range }
$raw = & git @arguments
if ($LASTEXITCODE -ne 0) { throw 'git log failed; is the range valid and the history fetched (fetch-depth: 0)?' }
$text = ($raw -join "`n")

$problems = New-Object System.Collections.Generic.List[string]
$checked = 0
foreach ($entry in ($text -split [char]0x1e)) {
    if ([string]::IsNullOrWhiteSpace($entry)) { continue }
    $parts = $entry.TrimStart("`n", "`r") -split [char]0x1f, 4
    if ($parts.Count -lt 4) { continue }
    $checked++
    $short = $parts[0].Substring(0, [Math]::Min(10, $parts[0].Length))
    if (-not (Test-Allowed $parts[1])) { $problems.Add("$short  the author e-mail is not a GitHub no-reply address") }
    if (-not (Test-Allowed $parts[2])) { $problems.Add("$short  the committer e-mail is not a GitHub no-reply address") }
    foreach ($line in ($parts[3] -split "`n")) {
        if ($line -match '^\s*(Co-authored-by|Signed-off-by)\s*:.*<([^>]+)>') {
            if (-not (Test-Allowed $Matches[2])) { $problems.Add("$short  a $($Matches[1]) trailer contains an address that is not a GitHub no-reply address") }
        }
    }
}

if ($problems.Count -gt 0) {
    Write-Host "Commit identity check FAILED ($($problems.Count) problem(s) in $checked commit(s)):"
    $problems | ForEach-Object { Write-Host "  $_" }
    Write-Host ''
    Write-Host 'Your git e-mail must be your GitHub no-reply address (GitHub > Settings > Emails shows it, form <id>+<username>@users.noreply.github.com).'
    Write-Host 'Fix: git config user.email "<id>+<username>@users.noreply.github.com", then rewrite the affected commits'
    Write-Host '(for the latest one: git commit --amend --reset-author --no-edit) and push again. Also turn on'
    Write-Host '"Block command line pushes that expose my email" in your GitHub e-mail settings.'
    exit 1
}
Write-Host "Commit identity check passed: $checked commit(s) use only GitHub no-reply addresses."
