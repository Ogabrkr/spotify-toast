# Janela de fundo usada só para gerar as imagens do README (papel de parede abstrato).
param([int]$W = 720, [int]$H = 520)
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
[System.Windows.Forms.Application]::EnableVisualStyles()

$form = New-Object System.Windows.Forms.Form
$form.FormBorderStyle = 'None'
$form.StartPosition = 'Manual'
$form.Location = New-Object System.Drawing.Point 0, 0
$form.Size = New-Object System.Drawing.Size $W, $H
$form.ShowInTaskbar = $false
$form.BackColor = [System.Drawing.Color]::FromArgb(18, 16, 40)

$form.Add_Paint({
    param($s, $e)
    $g = $e.Graphics
    $g.SmoothingMode = 'AntiAlias'
    $blobs = @(
        @(-80, -120, 420, 360, [System.Drawing.Color]::FromArgb(255, 255, 72, 150)),
        @(250, -60, 380, 300, [System.Drawing.Color]::FromArgb(255, 255, 170, 60)),
        @(430, 120, 420, 380, [System.Drawing.Color]::FromArgb(255, 70, 120, 255)),
        @(-60, 220, 420, 380, [System.Drawing.Color]::FromArgb(255, 120, 60, 230)),
        @(180, 300, 360, 300, [System.Drawing.Color]::FromArgb(255, 30, 200, 170))
    )
    foreach ($b in $blobs) {
        $path = New-Object System.Drawing.Drawing2D.GraphicsPath
        $path.AddEllipse([single]$b[0], [single]$b[1], [single]$b[2], [single]$b[3])
        $br = New-Object System.Drawing.Drawing2D.PathGradientBrush $path
        $br.CenterColor = $b[4]
        $br.SurroundColors = [System.Drawing.Color[]]@([System.Drawing.Color]::FromArgb(0, $b[4]))
        $g.FillPath($br, $path)
    }
    # linhas finas para o desfoque do vidro ficar visível
    $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(70, 255, 255, 255)), 2
    for ($x = -200; $x -lt $W; $x += 38) { $g.DrawLine($pen, $x, 0, $x + 200, $H) }
})
$form.Add_Shown({ $form.Activate() })
[System.Windows.Forms.Application]::Run($form)
