# Builds the square Nocturne icon (Assets/icon.png) and a multi-size app.ico
# from the source art at the repository root (icon.png).
param(
    [string]$SourcePath = (Join-Path $PSScriptRoot "..\icon.png"),
    [string]$OutDir = (Join-Path $PSScriptRoot "..\src\Nocturne\Assets")
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

$srcBitmap = [System.Drawing.Bitmap]::FromFile((Resolve-Path $SourcePath))
$size = 256
$canvas = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$g = [System.Drawing.Graphics]::FromImage($canvas)
$g.Clear([System.Drawing.Color]::Black)
$g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality

# Fit the artwork inside a 228 px box, keeping its aspect ratio, and center it.
$padding = 14
$box = $size - ($padding * 2)
$scale = [Math]::Min($box / $srcBitmap.Width, $box / $srcBitmap.Height)
$w = [int][Math]::Round($srcBitmap.Width * $scale)
$h = [int][Math]::Round($srcBitmap.Height * $scale)
$x = [int](($size - $w) / 2)
$y = [int](($size - $h) / 2)
$g.DrawImage($srcBitmap, $x, $y, $w, $h)
$g.Dispose()
$srcBitmap.Dispose()

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$canvas.Save((Join-Path $OutDir "icon.png"), [System.Drawing.Imaging.ImageFormat]::Png)
$canvas.Save((Join-Path $OutDir "app.png"), [System.Drawing.Imaging.ImageFormat]::Png)

# Multi-resolution .ico: every frame is stored as PNG.
$sizes = @(16, 24, 32, 48, 64, 128, 256)
$frames = @()
foreach ($s in $sizes) {
    $bmp = New-Object System.Drawing.Bitmap($s, $s, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $gg = [System.Drawing.Graphics]::FromImage($bmp)
    $gg.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $gg.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $gg.DrawImage($canvas, 0, 0, $s, $s)
    $gg.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $frames += ,@($s, $ms.ToArray())
    $bmp.Dispose()
    $ms.Dispose()
}
$canvas.Dispose()

$icoPath = Join-Path $OutDir "app.ico"
$fs = [System.IO.File]::Create($icoPath)
$bw = New-Object System.IO.BinaryWriter($fs)
$bw.Write([UInt16]0)                 # reserved
$bw.Write([UInt16]1)                 # type: icon
$bw.Write([UInt16]$frames.Count)
$offset = 6 + (16 * $frames.Count)
foreach ($frame in $frames) {
    $s = $frame[0]
    $data = $frame[1]
    $bw.Write([Byte]($(if ($s -ge 256) { 0 } else { $s })))
    $bw.Write([Byte]($(if ($s -ge 256) { 0 } else { $s })))
    $bw.Write([Byte]0)               # palette
    $bw.Write([Byte]0)               # reserved
    $bw.Write([UInt16]1)             # planes
    $bw.Write([UInt16]32)            # bpp
    $bw.Write([UInt32]$data.Length)
    $bw.Write([UInt32]$offset)
    $offset += $data.Length
}
foreach ($frame in $frames) { $bw.Write($frame[1]) }
$bw.Flush()
$bw.Dispose()
$fs.Dispose()

Write-Output "Wrote $OutDir\icon.png, app.png and app.ico"
