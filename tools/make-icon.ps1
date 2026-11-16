# Gera app.ico (quadrado verde com nota musical) com frames PNG em vários tamanhos.
param([string]$Out = (Join-Path $PSScriptRoot '..\app.ico'))
Add-Type -AssemblyName System.Drawing

function New-RoundRect([single]$x, [single]$y, [single]$w, [single]$h, [single]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    $p
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$frames = foreach ($sz in $sizes) {
    $bmp = New-Object System.Drawing.Bitmap $sz, $sz, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'; $g.PixelOffsetMode = 'HighQuality'
    $k = $sz / 16.0
    $bg = New-RoundRect (0.4 * $k) (0.4 * $k) (15.2 * $k) (15.2 * $k) (3.8 * $k)
    $rect = New-Object System.Drawing.RectangleF 0, 0, $sz, $sz
    $grad = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, ([System.Drawing.Color]::FromArgb(255, 36, 222, 112)), ([System.Drawing.Color]::FromArgb(255, 10, 104, 84)), 50.0
    $g.FillPath($grad, $bg)
    # mesma nota do app (espaço 16x16), reduzida e centralizada
    $g.TranslateTransform(2.7 * $k, 2.9 * $k); $g.ScaleTransform(0.66 * $k, 0.66 * $k)
    $n = New-Object System.Drawing.Drawing2D.GraphicsPath ([System.Drawing.Drawing2D.FillMode]::Winding)
    $n.AddEllipse(1.4, 10.2, 5.0, 4.0); $n.AddEllipse(8.9, 8.7, 5.0, 4.0)
    $n.AddRectangle((New-Object System.Drawing.RectangleF 5.2, 4.0, 1.3, 8.2))
    $n.AddRectangle((New-Object System.Drawing.RectangleF 12.6, 2.5, 1.3, 8.2))
    $pts = [System.Drawing.PointF[]]@((New-Object System.Drawing.PointF 5.2, 3.6), (New-Object System.Drawing.PointF 13.9, 1.6), (New-Object System.Drawing.PointF 13.9, 4.1), (New-Object System.Drawing.PointF 5.2, 6.1))
    $n.AddPolygon($pts)
    $g.FillPath([System.Drawing.Brushes]::White, $n)
    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    , @($sz, $ms.ToArray())
}

$ico = New-Object System.IO.MemoryStream
$bw = [System.IO.BinaryWriter]::new($ico)
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$frames.Count)
$offset = 6 + 16 * $frames.Count
foreach ($f in $frames) {
    $sz = $f[0]; $data = $f[1]
    $dim = if ($sz -ge 256) { 0 } else { $sz }
    $bw.Write([byte]$dim); $bw.Write([byte]$dim); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([uint16]1); $bw.Write([uint16]32)
    $bw.Write([uint32]$data.Length); $bw.Write([uint32]$offset)
    $offset += $data.Length
}
foreach ($f in $frames) { $bw.Write([byte[]]$f[1]) }
$bw.Flush()
[System.IO.File]::WriteAllBytes([System.IO.Path]::GetFullPath($Out), $ico.ToArray())
Write-Host "ok: $Out"
