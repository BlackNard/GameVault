[CmdletBinding()]
param([string]$ToolsDirectory = (Join-Path (Split-Path $PSScriptRoot -Parent) 'work/tools/inno-7.1.1'))
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$directory = [IO.Path]::GetFullPath($ToolsDirectory)
if ($directory -match '["\r\n]') { throw 'Unsafe tools directory.' }
New-Item -ItemType Directory -Force $directory | Out-Null
$package = Join-Path $directory 'compiler-setup.exe'
$sha256 = '8a5a6711c3766a6e5fb74f280ba0e3551e719ce94538d901fdbd1f8f9920b3f0'
if (-not (Test-Path -LiteralPath $package)) {
    Invoke-WebRequest 'https://github.com/jrsoftware/issrc/releases/download/is-7_1_1/innosetup-7.1.1-x64.exe' -OutFile $package
}
if ((Get-FileHash -LiteralPath $package -Algorithm SHA256).Hash -ne $sha256) { throw 'Compiler package SHA-256 mismatch.' }
$signature = Get-AuthenticodeSignature -LiteralPath $package
if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'O=Pyrsys B\.V\.') {
    throw 'The compiler package must have a valid Pyrsys B.V. Authenticode signature.'
}
$compiler = Join-Path $directory 'ISCC.exe'
if (-not (Test-Path -LiteralPath $compiler)) {
    # Official portable mode: no Add/Remove Programs registration, uninstaller, or shortcuts.
    $arguments = '/PORTABLE=1 /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CURRENTUSER /TASKS="" /DIR="' +
        $directory + '" /LOG="' + (Join-Path $directory 'bootstrap.log') + '"'
    $process = Start-Process -FilePath $package -ArgumentList $arguments -WindowStyle Hidden -PassThru -Wait
    if ($process.ExitCode -ne 0) { throw "Portable compiler bootstrap failed: $($process.ExitCode)" }
}
$compilerSignature = Get-AuthenticodeSignature -LiteralPath $compiler
if ($compilerSignature.Status -ne 'Valid' -or $compilerSignature.SignerCertificate.Subject -notmatch 'O=Pyrsys B\.V\.' -or
    (Get-FileHash -LiteralPath $compiler -Algorithm SHA256).Hash -ne 'e0f9f1dc32032650c6629f6dee41eb6d20ba140e4e3c07c25d8a24f9ba781644') {
    throw 'Unexpected installer compiler signature or version.'
}
return $compiler
