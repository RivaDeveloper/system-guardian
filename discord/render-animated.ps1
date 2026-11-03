# Renders the animated server icon and banner (assets/icon-animated.gif and assets/banner-animated.gif).
# Needs: the .NET SDK (to build tools/shot, a tiny WebView2 screenshot tool), Python (a local web server for the page's
# fonts) and ffmpeg (set $env:FFMPEG, or it looks for the copy that ships with Lumen Studios).
#   powershell -ExecutionPolicy Bypass -File .\render-animated.ps1
param([int]$IconFrames = 40, [int]$BannerFrames = 36, [int]$IconFps = 20, [int]$BannerFps = 18)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$ffmpeg = if ($env:FFMPEG) { $env:FFMPEG } elseif (Test-Path 'C:\Program Files\Lumen Studios\resources\bin\ffmpeg.exe') { 'C:\Program Files\Lumen Studios\resources\bin\ffmpeg.exe' } else { (Get-Command ffmpeg).Source }
$work = Join-Path $env:TEMP 'guardian-anim'; New-Item -ItemType Directory -Force $work | Out-Null

# 1. the screenshot tool
dotnet build "$repo\tools\shot\shot.csproj" -c Release | Out-Null
$shot = Get-ChildItem "$repo\tools\shot\bin\Release" -Filter shot.exe -Recurse | Select-Object -First 1 -ExpandProperty FullName

# 2. a local web server so the page can load its fonts
$port = 8766
$server = Start-Process python -ArgumentList "-m http.server $port --bind 127.0.0.1" -WorkingDirectory $repo -PassThru -WindowStyle Hidden
Start-Sleep 2
try {
  function Render($mode, $w, $h, $frames, $fps, $out) {
    Get-ChildItem $work -Filter "$mode-*.png" -ErrorAction SilentlyContinue | ForEach-Object { $_.Delete() }
    $env:FRAMES = $frames
    # shot.exe is a windowless app, so PowerShell would not wait for it without -Wait
    Start-Process $shot -ArgumentList "http://127.0.0.1:$port/discord/assets/art-anim.html?m=$mode", "`"$work\$mode.png`"", $w, $h -Wait
    Remove-Item Env:\FRAMES
    # one palette for the whole loop keeps colours steady; Bayer dithering keeps the gradients smooth and the file small
    & $ffmpeg -y -loglevel error -framerate $fps -i "$work\$mode-%03d.png" -vf "palettegen=stats_mode=full:max_colors=256" "$work\$mode-palette.png"
    & $ffmpeg -y -loglevel error -framerate $fps -i "$work\$mode-%03d.png" -i "$work\$mode-palette.png" -lavfi "paletteuse=dither=bayer:bayer_scale=4:diff_mode=rectangle" -loop 0 $out
    "{0}: {1:N1} MB" -f (Split-Path $out -Leaf), ((Get-Item $out).Length / 1MB)
  }
  Render 'icon'   256 256 $IconFrames   $IconFps   "$PSScriptRoot\assets\icon-animated.gif"
  Render 'banner' 960 540 $BannerFrames $BannerFps "$PSScriptRoot\assets\banner-animated.gif"
} finally { Stop-Process -Id $server.Id -Force -ErrorAction SilentlyContinue }
