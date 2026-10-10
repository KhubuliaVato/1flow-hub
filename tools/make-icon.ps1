# Generates assets\app.png (256px) and assets\app.ico (PNG-compressed) — run once, or after changing the design.
Add-Type -AssemblyName System.Drawing
$size = 256
$bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = 'AntiAlias'
$g.TextRenderingHint = 'AntiAliasGridFit'
$g.Clear([System.Drawing.Color]::Transparent)

# rounded square with a diagonal gradient
$r = 58
$path = New-Object System.Drawing.Drawing2D.GraphicsPath
$path.AddArc(0, 0, $r*2, $r*2, 180, 90)
$path.AddArc($size-$r*2, 0, $r*2, $r*2, 270, 90)
$path.AddArc($size-$r*2, $size-$r*2, $r*2, $r*2, 0, 90)
$path.AddArc(0, $size-$r*2, $r*2, $r*2, 90, 90)
$path.CloseFigure()
$rect = New-Object System.Drawing.Rectangle 0, 0, $size, $size
$grad = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, ([System.Drawing.Color]::FromArgb(124,140,255)), ([System.Drawing.Color]::FromArgb(155,231,255)), 45
$g.FillPath($grad, $path)

# soft highlight
$hl = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, ([System.Drawing.Color]::FromArgb(70,255,255,255)), ([System.Drawing.Color]::FromArgb(0,255,255,255)), 90
$g.FillPath($hl, $path)

# "1" glyph + flow wave
$ink = [System.Drawing.Color]::FromArgb(255, 10, 13, 42)
$pen = New-Object System.Drawing.Pen $ink, 22
$pen.StartCap = 'Round'; $pen.EndCap = 'Round'; $pen.LineJoin = 'Round'
$g.DrawLines($pen, @(
  (New-Object System.Drawing.PointF 92, 78),
  (New-Object System.Drawing.PointF 128, 56),
  (New-Object System.Drawing.PointF 128, 176)))
$wave = New-Object System.Drawing.Pen $ink, 16
$wave.StartCap = 'Round'; $wave.EndCap = 'Round'
$g.DrawBezier($wave, 70, 196, 100, 168, 130, 226, 186, 190)
$g.Dispose()

$assets = Join-Path $PSScriptRoot '..\assets'
New-Item -ItemType Directory -Force $assets | Out-Null
$png = Join-Path $assets 'app.png'
$bmp.Save($png, [System.Drawing.Imaging.ImageFormat]::Png)

# PNG-in-ICO container
$pngBytes = [System.IO.File]::ReadAllBytes($png)
$ms = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter $ms
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]1)       # ICONDIR
$bw.Write([Byte]0); $bw.Write([Byte]0); $bw.Write([Byte]0); $bw.Write([Byte]0)  # 256x256, no palette
$bw.Write([UInt16]1); $bw.Write([UInt16]32)
$bw.Write([UInt32]$pngBytes.Length); $bw.Write([UInt32]22)
$bw.Write($pngBytes)
[System.IO.File]::WriteAllBytes((Join-Path $assets 'app.ico'), $ms.ToArray())
$bmp.Dispose()
Write-Host "icon written to $assets"
