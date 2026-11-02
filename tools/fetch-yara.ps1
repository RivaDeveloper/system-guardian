# Downloads the YARA engine (yara64.exe, yarac64.exe) into Assets\yara so it ships with the app.
# The version is pinned and the archive is checked against a fixed SHA-256 before anything is extracted.
# The binaries are not committed (see .gitignore); build.ps1 runs this automatically.
#   powershell -File tools\fetch-yara.ps1
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$dest = Join-Path $root 'Assets\yara'

$version = '4.6.0'
$asset = 'yara-4.6.0-2443-win64.zip'
$sha256 = '5e7124f173ed442458429ac0f79a4db2c024cc183c595737d477a7575d81e6dc'
$url = "https://github.com/VirusTotal/yara/releases/download/v$version/$asset"

if ((Test-Path (Join-Path $dest 'yara64.exe')) -and (Test-Path (Join-Path $dest 'yarac64.exe')) -and (Test-Path (Join-Path $dest 'LICENSE-YARA.txt'))) {
    "YARA $version already present."
    return
}

New-Item -ItemType Directory -Force $dest | Out-Null
$tmp = Join-Path ([IO.Path]::GetTempPath()) ("guardian-yara-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory $tmp | Out-Null
try {
    $zip = Join-Path $tmp $asset
    Invoke-WebRequest $url -OutFile $zip -UseBasicParsing
    $actual = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLower()
    if ($actual -ne $sha256) { throw "Checksum mismatch for $asset (expected $sha256, got $actual). Not extracting." }

    Expand-Archive $zip (Join-Path $tmp 'x') -Force
    Copy-Item (Join-Path $tmp 'x\yara64.exe') $dest -Force
    Copy-Item (Join-Path $tmp 'x\yarac64.exe') $dest -Force
    Invoke-WebRequest "https://raw.githubusercontent.com/VirusTotal/yara/v$version/COPYING" -OutFile (Join-Path $dest 'LICENSE-YARA.txt') -UseBasicParsing
    "YARA $version installed to $dest"
    & (Join-Path $dest 'yara64.exe') --version
}
finally {
    Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
}
