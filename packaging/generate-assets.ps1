<#
.SYNOPSIS
  Regenerates the app icon (.ico) and the MSIX/Store logos in packaging\Assets\.
  Same glyph as TrayIcon.DrawIcon, drawn on a 32-unit grid and scaled. Run once after changing the design.
#>
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$out = Join-Path $PSScriptRoot 'Assets'
New-Item -ItemType Directory -Force -Path $out | Out-Null

function New-Glyph([int]$width, [int]$height, [double]$glyphSize) {
    $bmp = New-Object System.Drawing.Bitmap $width, $height
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.Clear([System.Drawing.Color]::Transparent)
    $s = $glyphSize / 32.0
    $g.TranslateTransform(($width - $glyphSize) / 2, ($height - $glyphSize) / 2)
    $g.ScaleTransform($s, $s)

    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $r = 7; $d = $r * 2
    $path.AddArc(2, 2, $d, $d, 180, 90)
    $path.AddArc(30 - $d, 2, $d, $d, 270, 90)
    $path.AddArc(30 - $d, 30 - $d, $d, $d, 0, 90)
    $path.AddArc(2, 30 - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    $g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(0x0E, 0x6B, 0x68))), $path)

    $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::White), 2.4
    $pen.LineJoin = 'Round'
    $g.DrawRectangle($pen, 9, 9, 14, 16)
    $white = [System.Drawing.Brushes]::White
    $g.FillRectangle($white, 12, 6, 8, 5)
    $g.FillRectangle($white, 12, 15, 8, 2)
    $g.FillRectangle($white, 12, 19, 6, 2)
    $g.Dispose()
    return $bmp
}

function Save-Png($bmp, $name) {
    $bmp.Save((Join-Path $out $name), [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
}

# MSIX / Store logos (the glyph fills ~2/3 of the tile, like Windows' own icons)
Save-Png (New-Glyph 44 44 44) 'Square44x44Logo.png'
Save-Png (New-Glyph 150 150 100) 'Square150x150Logo.png'
Save-Png (New-Glyph 310 150 100) 'Wide310x150Logo.png'
Save-Png (New-Glyph 50 50 50) 'StoreLogo.png'

# Multi-size .ico (PNG-compressed entries, supported since Windows Vista)
$sizes = 16, 24, 32, 48, 256
$images = foreach ($size in $sizes) {
    $bmp = New-Glyph $size $size $size
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    , $ms.ToArray()
}
$fs = [System.IO.File]::Create((Join-Path $out 'app.ico'))
$w = New-Object System.IO.BinaryWriter $fs
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $dim = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32)
    $w.Write([uint32]$images[$i].Length); $w.Write([uint32]$offset)
    $offset += $images[$i].Length
}
foreach ($img in $images) { $w.Write($img) }
$w.Dispose()

Write-Host "Assets written to $out"
