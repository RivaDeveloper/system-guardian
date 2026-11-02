# Generates the installer artwork (side panel + header orb) at several DPI sizes.
# Run from the repo root:  powershell -File installer\make-assets.ps1
Add-Type -AssemblyName System.Drawing
$root = Split-Path $PSScriptRoot -Parent
$out = Join-Path $PSScriptRoot 'assets'
New-Item -ItemType Directory -Force $out | Out-Null

$fonts = New-Object System.Drawing.Text.PrivateFontCollection
foreach ($f in 'Syne-ExtraBold.ttf', 'Manrope-Bold.ttf') { $fonts.AddFontFile((Join-Path $root "Assets\Fonts\$f")) }
$syne = $fonts.Families | Where-Object Name -eq 'Syne ExtraBold'
$manrope = $fonts.Families | Where-Object Name -eq 'Manrope'

function Hex($h, $a = 255) { $c = [System.Drawing.ColorTranslator]::FromHtml($h); [System.Drawing.Color]::FromArgb($a, $c.R, $c.G, $c.B) }

function Blob($g, $cx, $cy, $rx, $ry, $col, $alpha) {
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddEllipse($cx - $rx, $cy - $ry, 2 * $rx, 2 * $ry)
    $br = New-Object System.Drawing.Drawing2D.PathGradientBrush $path
    $br.CenterPoint = New-Object System.Drawing.PointF $cx, $cy
    $cb = New-Object System.Drawing.Drawing2D.ColorBlend 4
    $cb.Colors = @((Hex $col 0), (Hex $col ([int]($alpha * .25))), (Hex $col ([int]($alpha * .6))), (Hex $col $alpha))
    $cb.Positions = @(0.0, 0.35, 0.7, 1.0)
    $br.InterpolationColors = $cb
    $g.FillPath($br, $path)
}

# Lumen orb: rose -> violet -> cyan with a soft white core
function DrawOrb($g, $cx, $cy, $d) {
    $rect = New-Object System.Drawing.RectangleF ($cx - $d / 2), ($cy - $d / 2), $d, $d
    $lg = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, (Hex '#ff2e55'), (Hex '#38d0ff'), 45
    $cb = New-Object System.Drawing.Drawing2D.ColorBlend 3
    $cb.Colors = @((Hex '#ff2e55'), (Hex '#8b6bff'), (Hex '#38d0ff')); $cb.Positions = @(0.0, 0.55, 1.0)
    $lg.InterpolationColors = $cb
    $g.FillEllipse($lg, $rect)
    $g.FillEllipse((New-Object System.Drawing.SolidBrush (Hex '#ffffff' 70)), ($cx - $d * .36), ($cy - $d / 2), ($d * .5), $d)
    $g.FillEllipse((New-Object System.Drawing.SolidBrush (Hex '#ffffff' 70)), ($cx - $d * .22), ($cy - $d * .22), ($d * .44), ($d * .44))
    $g.FillEllipse((New-Object System.Drawing.SolidBrush (Hex '#ffffff' 235)), ($cx - $d * .2), ($cy - $d * .2), ($d * .4), ($d * .4))
}

function Spaced($g, $text, $font, $brush, $cx, $y, $tracking) {
    $w = 0; foreach ($ch in $text.ToCharArray()) { $w += $g.MeasureString([string]$ch, $font, 1000, [System.Drawing.StringFormat]::GenericTypographic).Width + $tracking }
    $x = $cx - ($w - $tracking) / 2
    foreach ($ch in $text.ToCharArray()) {
        $g.DrawString([string]$ch, $font, $brush, $x, $y, [System.Drawing.StringFormat]::GenericTypographic)
        $x += $g.MeasureString([string]$ch, $font, 1000, [System.Drawing.StringFormat]::GenericTypographic).Width + $tracking
    }
}

# ---- side panel, mastered at 4x (656 x 1256) then resampled ----
$S = 4; $W = 164 * $S; $H = 314 * $S
$master = New-Object System.Drawing.Bitmap $W, $H
$g = [System.Drawing.Graphics]::FromImage($master)
$g.SmoothingMode = 'AntiAlias'; $g.TextRenderingHint = 'AntiAliasGridFit'; $g.InterpolationMode = 'HighQualityBicubic'
$g.Clear((Hex '#06070c'))
Blob $g ($W * .10) ($H * .16) ($W * .95) ($H * .34) '#362079' 255      # violet, top left
Blob $g ($W * .95) ($H * .86) ($W * .90) ($H * .36) '#981b3a' 255      # rose, bottom right
Blob $g ($W * .70) ($H * .04) ($W * .60) ($H * .20) '#981b3a' 120
DrawOrb $g ($W / 2) ($H * .30) (74 * $S)
$title = New-Object System.Drawing.Font $syne, (18.5 * $S), ([System.Drawing.FontStyle]::Regular), ([System.Drawing.GraphicsUnit]::Pixel)
$sf = New-Object System.Drawing.StringFormat; $sf.Alignment = 'Center'
$g.DrawString('guardian', $title, (New-Object System.Drawing.SolidBrush (Hex '#eef0f7')), (New-Object System.Drawing.RectangleF 0, ($H * .30 + 52 * $S), $W, (34 * $S)), $sf)
$tag = New-Object System.Drawing.Font $manrope, (7.5 * $S), ([System.Drawing.FontStyle]::Bold), ([System.Drawing.GraphicsUnit]::Pixel)
Spaced $g 'SYSTEM CARE' $tag (New-Object System.Drawing.SolidBrush (Hex '#ff2e55')) ($W / 2) ($H * .30 + 92 * $S) (2.2 * $S)
$small = New-Object System.Drawing.Font $manrope, (7.5 * $S), ([System.Drawing.FontStyle]::Bold), ([System.Drawing.GraphicsUnit]::Pixel)
Spaced $g 'SCAN  .  UPDATE  .  REPAIR' $small (New-Object System.Drawing.SolidBrush (Hex '#a2a8bc')) ($W / 2) ($H - 26 * $S) (1.4 * $S)
$g.Dispose()

$scales = @(@(100, 164, 314), @(125, 205, 393), @(150, 246, 471), @(175, 287, 550), @(200, 328, 628))
foreach ($s in $scales) {
    $bmp = New-Object System.Drawing.Bitmap $s[1], $s[2], ([System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
    $gg = [System.Drawing.Graphics]::FromImage($bmp); $gg.InterpolationMode = 'HighQualityBicubic'; $gg.PixelOffsetMode = 'HighQuality'
    $gg.DrawImage($master, 0, 0, $s[1], $s[2]); $gg.Dispose()
    $bmp.Save((Join-Path $out ("panel-$($s[0]).bmp")), [System.Drawing.Imaging.ImageFormat]::Bmp); $bmp.Dispose()
}
$master.Dispose()

# ---- header orb (transparent PNG) ----
foreach ($px in 55, 69, 83, 96, 110, 138) {
    $big = New-Object System.Drawing.Bitmap ($px * 4), ($px * 4), ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $gg = [System.Drawing.Graphics]::FromImage($big); $gg.SmoothingMode = 'AntiAlias'; $gg.Clear([System.Drawing.Color]::Transparent)
    DrawOrb $gg ($px * 2) ($px * 2) ($px * 4 * .78); $gg.Dispose()
    $bmp = New-Object System.Drawing.Bitmap $px, $px, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g2 = [System.Drawing.Graphics]::FromImage($bmp); $g2.InterpolationMode = 'HighQualityBicubic'; $g2.PixelOffsetMode = 'HighQuality'
    $g2.DrawImage($big, 0, 0, $px, $px); $g2.Dispose(); $big.Dispose()
    $bmp.Save((Join-Path $out "orb-$px.png"), [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
}
Get-ChildItem $out | Select-Object Name, Length
