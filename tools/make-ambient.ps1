# Regenerates Assets\ambient.png, the app's background.  Run from the repo root:
#   powershell -File tools\make-ambient.ps1
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
. (Join-Path $PSScriptRoot 'AmbientLib.ps1')
$bmp = [Ambient]::Render(2560, 1440, 7)
$bmp.Save((Join-Path $root 'Assets\ambient.png'), [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
"Wrote Assets\ambient.png ($([math]::Round((Get-Item (Join-Path $root 'Assets\ambient.png')).Length / 1KB)) KB)"
