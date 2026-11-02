# Builds the release installer: dist\Guardian-Setup-<version>.exe
# The version comes from <Version> in Guardian.csproj, so bump it there before releasing.
# Needs the .NET 9 SDK and Inno Setup 6 (winget install JRSoftware.InnoSetup).
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)

$version = ([xml](Get-Content Guardian.csproj)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not $version) { throw 'No <Version> found in Guardian.csproj' }

$iscc = @("$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe", "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe") |
    Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw 'Inno Setup 6 not found' }

if (Test-Path publish) { Remove-Item publish -Recurse -Force }
dotnet publish Guardian.csproj -c Release -p:Platform=x64 -r win-x64 --self-contained true -o publish
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed' }

& $iscc "/DAppVersion=$version" installer\guardian.iss
if ($LASTEXITCODE -ne 0) { throw 'ISCC failed' }

$file = "dist\Guardian-Setup-$version.exe"
"Built $file ($([math]::Round((Get-Item $file).Length / 1MB, 1)) MB)"
"SHA-256: $((Get-FileHash $file -Algorithm SHA256).Hash.ToLower())"
