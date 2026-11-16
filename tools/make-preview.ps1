# Gera as imagens do README em docs/: GIF da animação, grade de estilos e o menu do tray.
# Usa o build Release, uma janela de fundo colorida e a música de exemplo (--sample).
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$exe = Join-Path $root 'bin\Release\net10.0-windows10.0.19041.0\win-x64\SpotifyToast.exe'
$docs = Join-Path $root 'docs'
$tmp = Join-Path $env:TEMP 'spotifytoast-preview'
New-Item -ItemType Directory -Force $docs, $tmp | Out-Null
Get-ChildItem $tmp -Filter *.png | Remove-Item

Add-Type -AssemblyName System.Drawing
Add-Type -ReferencedAssemblies System.Drawing @"
using System; using System.Drawing; using System.Runtime.InteropServices;
public static class Shot {
  [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr h);
  [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr h, IntPtr dc);
  [DllImport("gdi32.dll")] static extern bool BitBlt(IntPtr d, int x, int y, int w, int h, IntPtr s, int sx, int sy, int rop);
  public static void Save(int x, int y, int w, int h, string path) {
    using (var bmp = new Bitmap(w, h)) {
      using (var g = Graphics.FromImage(bmp)) { var dst = g.GetHdc(); var src = GetDC(IntPtr.Zero);
        BitBlt(dst, 0, 0, w, h, src, x, y, 0x00CC0020 | 0x40000000); ReleaseDC(IntPtr.Zero, src); g.ReleaseHdc(dst); }
      bmp.Save(path); } } }
"@

$settingsFile = Join-Path $env:APPDATA 'SpotifyToast\settings.json'
New-Item -ItemType Directory -Force (Split-Path $settingsFile) | Out-Null
$backup = if (Test-Path $settingsFile) { Get-Content $settingsFile -Raw } else { $null }
function Set-Look([bool]$glass, [string]$theme, [int]$secs = 8) {
    $json = @{ Glass = $glass; Theme = $theme; Position = 'left'; DurationSeconds = $secs; ShowInFullscreen = $true } | ConvertTo-Json
    [IO.File]::WriteAllText($settingsFile, $json)
}
function Stop-App { Get-Process SpotifyToast -ErrorAction SilentlyContinue | Stop-Process -Force; Start-Sleep -Milliseconds 400 }

$bd = Start-Process powershell -ArgumentList '-NoProfile', '-STA', '-ExecutionPolicy', 'Bypass', '-File', (Join-Path $PSScriptRoot 'backdrop.ps1') -PassThru -WindowStyle Hidden
Start-Sleep 2

try {
    # aquece (a 1ª execução depois do build é lenta por causa do antivírus)
    Stop-App; Start-Process $exe; Start-Sleep 5; Stop-App

    # 1) grade de estilos
    $looks = @(@($true, 'dark', 'glass-dark'), @($true, 'light', 'glass-light'), @($false, 'dark', 'solid-dark'), @($false, 'light', 'solid-light'))
    foreach ($l in $looks) {
        Set-Look $l[0] $l[1]
        Stop-App; Start-Process $exe -ArgumentList '--sample'; Start-Sleep -Milliseconds 3600
        [Shot]::Save(0, 0, 410, 124, (Join-Path $tmp "$($l[2]).png"))
    }

    # 2) animação (entra, espera, sai)
    Set-Look $true 'dark' 2
    Stop-App; Start-Process $exe -ArgumentList '--sample'
    Start-Sleep -Milliseconds 1100
    $sw = [Diagnostics.Stopwatch]::StartNew(); $i = 0
    while ($sw.ElapsedMilliseconds -lt 4200) {
        [Shot]::Save(0, 0, 410, 124, (Join-Path $tmp ('f{0:D4}.png' -f $i))); $i++
        Start-Sleep -Milliseconds 20
    }
    $fps = [math]::Round($i / ($sw.ElapsedMilliseconds / 1000.0), 2)

    # 3) menu do tray
    Set-Look $true 'dark'
    Stop-App; Start-Process $exe -ArgumentList '--show-menu'; Start-Sleep -Milliseconds 3200
    [Shot]::Save(34, 34, 236, 236, (Join-Path $tmp 'menu.png'))
}
finally {
    Stop-App
    Stop-Process $bd -Force -ErrorAction SilentlyContinue
    if ($null -ne $backup) { [IO.File]::WriteAllText($settingsFile, $backup) } else { Remove-Item $settingsFile -ErrorAction SilentlyContinue }
}

# GIF com paleta otimizada
$ff = (Get-Command ffmpeg).Source
& $ff -y -loglevel error -framerate $fps -i (Join-Path $tmp 'f%04d.png') `
    -vf "fps=30,scale=820:-1:flags=lanczos,split[a][b];[a]palettegen=stats_mode=diff[p];[b][p]paletteuse=dither=sierra2_4a" `
    (Join-Path $docs 'preview.gif')

# grade 2x2 com legenda
$cells = 'glass-dark', 'glass-light', 'solid-dark', 'solid-light'
$labels = 'Liquid Glass · escuro', 'Liquid Glass · claro', 'Sólido · escuro', 'Sólido · claro'
$grid = New-Object System.Drawing.Bitmap (410 * 2 + 30), (124 * 2 + 30 + 60)
$g = [System.Drawing.Graphics]::FromImage($grid)
$g.Clear([System.Drawing.Color]::FromArgb(13, 17, 23)); $g.TextRenderingHint = 'AntiAlias'
$font = New-Object System.Drawing.Font 'Segoe UI Semibold', 11
for ($k = 0; $k -lt 4; $k++) {
    $cx = 10 + ($k % 2) * 420; $cy = 10 + [math]::Floor($k / 2) * 164
    $img = [System.Drawing.Image]::FromFile((Join-Path $tmp "$($cells[$k]).png"))
    $g.DrawImage($img, $cx, $cy, 410, 124); $img.Dispose()
    $g.DrawString($labels[$k], $font, [System.Drawing.Brushes]::Gainsboro, $cx + 4, $cy + 128)
}
$grid.Save((Join-Path $docs 'styles.png')); $grid.Dispose()
Copy-Item (Join-Path $tmp 'menu.png') (Join-Path $docs 'menu.png') -Force
Get-ChildItem $docs | Select-Object Name, @{n = 'KB'; e = { [math]::Round($_.Length / 1KB) } }
"frames=$i fps=$fps"
