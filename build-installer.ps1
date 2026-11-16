# Gera dist\SpotifyToast-Setup-<versão>.exe (instalador único, já com o .NET embutido).
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

[xml]$proj = Get-Content .\SpotifyToast.csproj
$version = ($proj.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version

Get-Process SpotifyToast -ErrorAction SilentlyContinue | Stop-Process -Force
if (Test-Path publish) { Remove-Item publish -Recurse -Force }

dotnet publish .\SpotifyToast.csproj -c Release -r win-x64 --self-contained true `
    -p:PublishReadyToRun=true -o publish
if ($LASTEXITCODE -ne 0) { throw 'falha no dotnet publish' }

$iscc = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw 'Inno Setup 6 não encontrado (winget install JRSoftware.InnoSetup)' }

& $iscc "/DAppVersion=$version" .\installer\SpotifyToast.iss
if ($LASTEXITCODE -ne 0) { throw 'falha no Inno Setup' }

Get-Item ".\dist\SpotifyToast-Setup-$version.exe" | Select-Object Name, @{n = 'MB'; e = { [math]::Round($_.Length / 1MB, 1) } }
