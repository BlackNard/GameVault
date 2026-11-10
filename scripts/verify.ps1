[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = Split-Path -Parent $PSScriptRoot
$runId = [Guid]::NewGuid().ToString('N')
$artifacts = Join-Path $root 'artifacts'
$publishDirectory = Join-Path $artifacts "publish-$runId"

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
    Invoke-Dotnet -Arguments @('test', 'tests/GameVault.Tests/GameVault.Tests.csproj',
        '-c', 'Release', '--no-build', '--no-restore', '--logger', 'trx;LogFileName=foundation.trx',
        '--results-directory', (Join-Path $artifacts "test-results-$runId"))
    Invoke-Dotnet -Arguments @('publish', 'src/GameVault.App/GameVault.App.csproj',
        '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true', '--no-restore',
        '-o', $publishDirectory)

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
        runId = $runId
        publishDirectory = $publishDirectory
        executable = Join-Path $publishDirectory 'GameVault.exe'
        verifiedUtc = [DateTime]::UtcNow.ToString('o')
    } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $artifacts 'verification.json') -Encoding utf8
    Write-Host "Verification passed. Published app: $publishDirectory"
}
finally {
    Pop-Location
}
