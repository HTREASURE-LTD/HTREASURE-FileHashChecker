<#
.SYNOPSIS
    Generates the HT File Hash Checker logo, application icon and installer wizard images.

.DESCRIPTION
    Writes:
      assets\branding\ht_logo.png        (256x256, About window and installer)
      assets\branding\ht_logo_64.png     (64x64, main window header)
      assets\icons\htfilehashchecker.ico (16/20/24/32/40/48/64 BMP frames + 256 PNG frame)
      installer\images\wizard-large-100.bmp / -200.bmp (Welcome/Finish page panel)
      installer\images\wizard-small-100.bmp / -200.bmp (inner page corner image)

    The artwork is drawn from the HTREASURE LTD palette (navy #0B3D91, blue #1976D2, white): a shield with a
    check mark over a hash symbol. To use official artwork, replace the files with images of the same names
    and sizes and rebuild; no code changes are needed.
#>
param([string]$Root = (Split-Path -Parent $PSScriptRoot))

Add-Type -AssemblyName System.Drawing
$ErrorActionPreference = 'Stop'

$navy  = [System.Drawing.ColorTranslator]::FromHtml('#0B3D91')
$blue  = [System.Drawing.ColorTranslator]::FromHtml('#1976D2')
$white = [System.Drawing.Color]::White

function New-RoundedRect([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = 2 * $r
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

function New-LogoBitmap([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)

    $s = [float]$size
    $tile = New-RoundedRect 0 0 ($s - 1) ($s - 1) ([Math]::Max(2.0, $s * 0.2))
    $rect = New-Object System.Drawing.RectangleF(0, 0, $s, $s)
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush($rect, $blue, $navy, 45.0)
    $g.FillPath($brush, $tile)

    # Shield
    $cx = $s / 2.0
    $top = $s * 0.16; $bottom = $s * 0.86; $half = $s * 0.30
    $shield = New-Object System.Drawing.Drawing2D.GraphicsPath
    $shield.AddLine([float]($cx), [float]$top, [float]($cx + $half), [float]($top + $s * 0.09))
    $shield.AddBezier([float]($cx + $half), [float]($top + $s * 0.09), [float]($cx + $half), [float]($s * 0.62),
        [float]($cx + $s * 0.14), [float]($s * 0.76), [float]$cx, [float]$bottom)
    $shield.AddBezier([float]$cx, [float]$bottom, [float]($cx - $s * 0.14), [float]($s * 0.76),
        [float]($cx - $half), [float]($s * 0.62), [float]($cx - $half), [float]($top + $s * 0.09))
    $shield.CloseFigure()
    $g.FillPath((New-Object System.Drawing.SolidBrush($white)), $shield)

    # Check mark in navy
    $penW = [Math]::Max(1.6, $s * 0.085)
    $pen = New-Object System.Drawing.Pen($navy, [float]$penW)
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $pts = [System.Drawing.PointF[]]@(
        (New-Object System.Drawing.PointF([float]($cx - $s * 0.14), [float]($s * 0.50))),
        (New-Object System.Drawing.PointF([float]($cx - $s * 0.03), [float]($s * 0.61))),
        (New-Object System.Drawing.PointF([float]($cx + $s * 0.16), [float]($s * 0.38))))
    $g.DrawLines($pen, $pts)

    # Small hash mark in the corner for larger sizes
    if ($size -ge 48) {
        $hp = New-Object System.Drawing.Pen($white, [float]([Math]::Max(1.0, $s * 0.025)))
        $ox = $s * 0.74; $oy = $s * 0.70; $hs = $s * 0.13
        $g.DrawLine($hp, [float]($ox + $hs * 0.35), [float]$oy, [float]($ox + $hs * 0.15), [float]($oy + $hs))
        $g.DrawLine($hp, [float]($ox + $hs * 0.75), [float]$oy, [float]($ox + $hs * 0.55), [float]($oy + $hs))
        $g.DrawLine($hp, [float]$ox, [float]($oy + $hs * 0.33), [float]($ox + $hs), [float]($oy + $hs * 0.33))
        $g.DrawLine($hp, [float]($ox - $hs * 0.05), [float]($oy + $hs * 0.68), [float]($ox + $hs * 0.95), [float]($oy + $hs * 0.68))
    }
    $g.Dispose()
    return $bmp
}

function Get-DibBytes([System.Drawing.Bitmap]$bmp) {
    $w = $bmp.Width; $h = $bmp.Height
    $ms = New-Object System.IO.MemoryStream
    $bw = New-Object System.IO.BinaryWriter($ms)
    $bw.Write([int]40); $bw.Write([int]$w); $bw.Write([int]($h * 2)); $bw.Write([int16]1); $bw.Write([int16]32)
    $bw.Write([int]0); $bw.Write([int]0); $bw.Write([int]0); $bw.Write([int]0); $bw.Write([int]0); $bw.Write([int]0)
    for ($y = $h - 1; $y -ge 0; $y--) {
        for ($x = 0; $x -lt $w; $x++) {
            $p = $bmp.GetPixel($x, $y)
            $bw.Write([byte]$p.B); $bw.Write([byte]$p.G); $bw.Write([byte]$p.R); $bw.Write([byte]$p.A)
        }
    }
    $maskStride = [int]([Math]::Ceiling($w / 32.0) * 4)
    $bw.Write((New-Object byte[] ($maskStride * $h)))
    $bw.Flush()
    return ,$ms.ToArray()
}

function Get-PngBytes([System.Drawing.Bitmap]$bmp) {
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    return ,$ms.ToArray()
}

function Save-Bmp24($bmp, $path) {
    $flat = New-Object System.Drawing.Bitmap($bmp.Width, $bmp.Height, [System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
    $g = [System.Drawing.Graphics]::FromImage($flat); $g.DrawImage($bmp, 0, 0, $bmp.Width, $bmp.Height); $g.Dispose()
    $flat.Save($path, [System.Drawing.Imaging.ImageFormat]::Bmp); $flat.Dispose()
}

$branding = Join-Path $Root 'assets\branding'
$icons = Join-Path $Root 'assets\icons'
$images = Join-Path $Root 'installer\images'
New-Item -ItemType Directory -Force $branding, $icons, $images | Out-Null

$logo = New-LogoBitmap 256
$logo.Save((Join-Path $branding 'ht_logo.png'), [System.Drawing.Imaging.ImageFormat]::Png)
$logo64 = New-LogoBitmap 64
$logo64.Save((Join-Path $branding 'ht_logo_64.png'), [System.Drawing.Imaging.ImageFormat]::Png)
$logo64.Dispose()

$sizes = 16, 20, 24, 32, 40, 48, 64, 256
$frames = @()
foreach ($s in $sizes) {
    $b = New-LogoBitmap $s
    if ($s -eq 256) { $frames += ,(Get-PngBytes $b) } else { $frames += ,(Get-DibBytes $b) }
    $b.Dispose()
}
$ico = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter($ico)
$w.Write([int16]0); $w.Write([int16]1); $w.Write([int16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]
    $dim = if ($s -ge 256) { 0 } else { $s }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([int16]1); $w.Write([int16]32)
    $w.Write([int]$frames[$i].Length); $w.Write([int]$offset)
    $offset += $frames[$i].Length
}
foreach ($f in $frames) { $w.Write([byte[]]$f) }
$w.Flush()
[System.IO.File]::WriteAllBytes((Join-Path $icons 'htfilehashchecker.ico'), $ico.ToArray())

# Installer wizard images
foreach ($scale in 1, 2) {
    $wd = 164 * $scale; $ht = 314 * $scale
    $bmp = New-Object System.Drawing.Bitmap($wd, $ht)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'; $g.TextRenderingHint = 'AntiAliasGridFit'
    $grad = New-Object System.Drawing.Drawing2D.LinearGradientBrush((New-Object System.Drawing.Rectangle(0, 0, $wd, $ht)), $blue, $navy, 90.0)
    $g.FillRectangle($grad, 0, 0, $wd, $ht)
    $size = 96 * $scale; $left = [int](($wd - $size) / 2)
    $g.DrawImage($logo, $left, 44 * $scale, $size, $size)
    $fmt = New-Object System.Drawing.StringFormat; $fmt.Alignment = 'Center'
    $titleFont = [System.Drawing.Font]::new('Segoe UI Semibold', [single](13 * $scale), [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
    $subFont = [System.Drawing.Font]::new('Segoe UI', [single](10 * $scale), [System.Drawing.FontStyle]::Regular, [System.Drawing.GraphicsUnit]::Pixel)
    $g.DrawString('HT File Hash', $titleFont, [System.Drawing.Brushes]::White, [System.Drawing.RectangleF]::new(0, [single](156 * $scale), $wd, [single](20 * $scale)), $fmt)
    $g.DrawString('Checker', $titleFont, [System.Drawing.Brushes]::White, [System.Drawing.RectangleF]::new(0, [single](175 * $scale), $wd, [single](20 * $scale)), $fmt)
    $g.DrawString('HTREASURE LTD', $subFont, [System.Drawing.Brushes]::White, [System.Drawing.RectangleF]::new(0, [single](202 * $scale), $wd, [single](18 * $scale)), $fmt)
    $g.Dispose()
    Save-Bmp24 $bmp (Join-Path $images ("wizard-large-{0}.bmp" -f ($scale * 100))); $bmp.Dispose()

    $s = 55 * $scale
    $small = New-Object System.Drawing.Bitmap($s, $s)
    $g = [System.Drawing.Graphics]::FromImage($small); $g.Clear($white); $g.SmoothingMode = 'AntiAlias'
    $pad = 3 * $scale
    $g.DrawImage($logo, $pad, $pad, $s - 2 * $pad, $s - 2 * $pad); $g.Dispose()
    Save-Bmp24 $small (Join-Path $images ("wizard-small-{0}.bmp" -f ($scale * 100))); $small.Dispose()
}
$logo.Dispose()
Write-Host "Branding written to $branding, $icons and $images"
