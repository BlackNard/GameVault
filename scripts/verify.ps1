[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = Split-Path -Parent $PSScriptRoot
$runId = [Guid]::NewGuid().ToString('N')
$artifacts = Join-Path $root 'artifacts'
$publishDirectory = Join-Path $artifacts "publish-$runId"
$workflowEvidence = Join-Path $artifacts "workflow-$runId"

function Invoke-Dotnet {
    param([string[]]$Arguments)
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments[0]) failed with exit code $LASTEXITCODE."
    }
}

Push-Location $root
try {
    Invoke-Dotnet -Arguments @('restore', 'GameVault.slnx', '--locked-mode')
    Invoke-Dotnet -Arguments @('build', 'GameVault.slnx', '-c', 'Release', '--no-restore')
    $previousEvidence = [Environment]::GetEnvironmentVariable('GAMEVAULT_TEST_EVIDENCE_DIRECTORY', 'Process')
    try {
        [Environment]::SetEnvironmentVariable('GAMEVAULT_TEST_EVIDENCE_DIRECTORY', $workflowEvidence, 'Process')
    Invoke-Dotnet -Arguments @('test', 'GameVault.slnx',
        '-c', 'Release', '--no-build', '--no-restore', '--logger', 'trx',
        '--results-directory', (Join-Path $artifacts "test-results-$runId"))
    }
    finally {
        [Environment]::SetEnvironmentVariable('GAMEVAULT_TEST_EVIDENCE_DIRECTORY', $previousEvidence, 'Process')
    }
    foreach ($scenario in @('full-workflow', 'failure-boundaries')) {
        $reportFile = Join-Path $workflowEvidence "$scenario.json"
        if (-not (Test-Path -LiteralPath $reportFile -PathType Leaf)) { throw "Missing automated workflow evidence: $scenario" }
        $report = Get-Content -Raw -LiteralPath $reportFile | ConvertFrom-Json
        if ($report.schemaVersion -ne 1 -or $report.scenario -ne $scenario -or $report.automatedPassed -ne $true -or
            $report.realGameValidated -ne $false -or $report.uiValidated -ne $false) {
            throw "Invalid automated-only workflow evidence: $scenario"
        }
        Write-Host ("Automated workflow passed: " + $scenario + " " + ($report.observations | ConvertTo-Json -Compress))
    }
    Invoke-Dotnet -Arguments @('publish', 'src/GameVault.App/GameVault.App.csproj',
        '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true', '--no-restore',
        '-o', $publishDirectory)

    & (Join-Path $PSScriptRoot 'copy-runtime-notices.ps1') -PublishDirectory $publishDirectory
    & (Join-Path $PSScriptRoot 'copy-capture-prerequisites.ps1') -PublishDirectory $publishDirectory

    foreach ($file in @('GameVault.exe', 'GameVault.dll', 'GameVault.deps.json',
        'GameVault.runtimeconfig.json', 'coreclr.dll', 'hostfxr.dll', 'PresentationFramework.dll')) {
        if (-not (Test-Path -LiteralPath (Join-Path $publishDirectory $file) -PathType Leaf)) {
            throw "The self-contained publish is missing $file."
        }
    }

    $userData = @(Get-ChildItem -LiteralPath $publishDirectory -Directory -Recurse |
        Where-Object { $_.Name -in @('Data', 'Profiles') })
    if ($userData.Count -gt 0) {
        throw 'The publish unexpectedly contains user data directories.'
    }

    $runtime = Get-Content -Raw -LiteralPath (Join-Path $publishDirectory 'GameVault.runtimeconfig.json') |
        ConvertFrom-Json
    if (-not $runtime.runtimeOptions.PSObject.Properties['includedFrameworks']) {
        throw 'The publish does not declare its bundled runtime frameworks.'
    }

    [pscustomobject]@{
        schemaVersion = 1
        runId = $runId
        automatedWorkflowsPassed = $true
        evidenceDirectory = $workflowEvidence
        ownerRealGameValidation = 'pending-owner-final-pass'
        ownerVisualUsabilityValidation = 'pending-owner-final-pass'
        issue18PerformanceAcceptance = 'pending'
        firstVersionAccepted = $false
        verifiedUtc = [DateTime]::UtcNow.ToString('o')
    } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $artifacts 'validation.json') -Encoding utf8
    [pscustomobject]@{
        runId = $runId
        publishDirectory = $publishDirectory
        executable = Join-Path $publishDirectory 'GameVault.exe'
        workflowEvidenceDirectory = $workflowEvidence
        verifiedUtc = [DateTime]::UtcNow.ToString('o')
    } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $artifacts 'verification.json') -Encoding utf8
    Write-Host "Verification passed. Published app: $publishDirectory"
    Write-Host "Automated workflows passed. Owner real-game/UI and first-version acceptance remain pending."
}
finally {
    Pop-Location
}
