$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$assetPath = Join-Path $PSScriptRoot '..\LyricFloat.App\Assets'
New-Item -ItemType Directory -Force -Path $assetPath | Out-Null
$frames = @()
foreach ($size in @(16,24,32,48,64,128,256)) {
    $bitmap = [System.Drawing.Bitmap]::new($size, $size)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.Clear([System.Drawing.Color]::Transparent)
    $background = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(255,24,30,42))
    $ink = [System.Drawing.Pen]::new([System.Drawing.Color]::White, [single]($size * .09))
    $accent = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(255,94,224,199))
    $graphics.FillEllipse($background, [single]1, [single]1, [single]($size-2), [single]($size-2))
    # Original L monogram with a floating accent, drawn entirely from basic geometry.
    $graphics.DrawLine($ink, [single]($size*.34), [single]($size*.27), [single]($size*.34), [single]($size*.68))
    $graphics.DrawLine($ink, [single]($size*.30), [single]($size*.68), [single]($size*.65), [single]($size*.68))
    $graphics.FillEllipse($accent, [single]($size*.54), [single]($size*.26), [single]($size*.18), [single]($size*.18))
    $stream = [System.IO.MemoryStream]::new()
    $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    $frames += ,@{ Size=$size; Bytes=$stream.ToArray() }
    $stream.Dispose(); $accent.Dispose(); $ink.Dispose(); $background.Dispose(); $graphics.Dispose(); $bitmap.Dispose()
}
$output = [System.IO.File]::Create((Join-Path $assetPath 'LyricFloat.ico'))
$writer = [System.IO.BinaryWriter]::new($output)
try {
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$frames.Count)
    $offset = 6 + 16 * $frames.Count
    foreach ($frame in $frames) {
        $dimension = if ($frame.Size -eq 256) { 0 } else { $frame.Size }
        $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
        $writer.Write([byte]0); $writer.Write([byte]0); $writer.Write([uint16]1); $writer.Write([uint16]32)
        $writer.Write([uint32]$frame.Bytes.Length); $writer.Write([uint32]$offset)
        $offset += $frame.Bytes.Length
    }
    foreach ($frame in $frames) { $writer.Write([byte[]]$frame.Bytes) }
} finally { $writer.Dispose(); $output.Dispose() }
