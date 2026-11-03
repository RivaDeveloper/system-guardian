# Starts the bot in this window (Ctrl+C stops it). Needs Node.js 22.5 or newer and a filled-in .env next to this file.
Set-Location -LiteralPath $PSScriptRoot
if (-not (Test-Path .env)) { Write-Host 'Copy .env.example to .env and fill it in first.' -ForegroundColor Yellow; exit 1 }
if (-not (Test-Path node_modules)) { npm install }
node src/index.js
