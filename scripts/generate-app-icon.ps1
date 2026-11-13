# Render the original path-only SVG using Windows WPF; no external graphics tools.
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore,WindowsBase
$assets = Join-Path (Split-Path $PSScriptRoot -Parent) 'src/GameVault.App/Assets'
[xml]$svg = Get-Content -LiteralPath (Join-Path $assets 'GameVault.svg') -Raw
$frames = @()
foreach ($size in @(16,24,32,48,64,128,256)) {
    $visual = [System.Windows.Media.DrawingVisual]::new()
    $context = $visual.RenderOpen()
    try {
        $context.PushTransform([System.Windows.Media.ScaleTransform]::new($size/256.0,$size/256.0))
        foreach ($path in $svg.svg.path) {
            $brush = [System.Windows.Media.BrushConverter]::new().ConvertFromInvariantString($path.fill)
            $geometry = [System.Windows.Media.Geometry]::Parse($path.d)
            $context.DrawGeometry($brush,$null,$geometry)
        }
        $context.Pop()
    } finally { $context.Close() }
    $bitmap = [System.Windows.Media.Imaging.RenderTargetBitmap]::new($size,$size,96,96,[System.Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($visual)
    $encoder = [System.Windows.Media.Imaging.PngBitmapEncoder]::new()
    $encoder.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $stream = [IO.MemoryStream]::new()
    try {$encoder.Save($stream);$frames += [pscustomobject]@{Size=$size;Bytes=$stream.ToArray()}}finally{$stream.Dispose()}
}
$destination = [IO.File]::Create((Join-Path $assets 'GameVault.ico'))
$writer = [IO.BinaryWriter]::new($destination)
try {
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$frames.Count)
    $offset = 6 + 16*$frames.Count
    foreach ($frame in $frames) {
        $dimension = if ($frame.Size -eq 256) {0} else {$frame.Size}
        $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
        $writer.Write([byte]0); $writer.Write([byte]0); $writer.Write([uint16]1); $writer.Write([uint16]32)
        $writer.Write([uint32]$frame.Bytes.Length); $writer.Write([uint32]$offset)
        $offset += $frame.Bytes.Length
    }
    foreach ($frame in $frames) {$writer.Write([byte[]]$frame.Bytes)}
} finally {$writer.Dispose()}
Write-Host 'Generated multi-size GameVault.ico (16–256px).'
