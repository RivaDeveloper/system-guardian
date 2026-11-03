# Asks for the bot token and the server id, then runs setup.mjs from this folder.
# Right-click > Run with PowerShell, or:  powershell -ExecutionPolicy Bypass -File .\run-setup.ps1
Set-Location -LiteralPath $PSScriptRoot

if (-not (Get-Command node -ErrorAction SilentlyContinue)) {
  Write-Host 'Node.js is not installed. Get it from https://nodejs.org (the LTS version), then run this again.' -ForegroundColor Yellow
  Read-Host 'Press Enter to close'; exit 1
}

$guild = Read-Host 'Server ID (Discord > right-click the server > Copy Server ID)'
$secure = Read-Host 'Bot token (it is hidden while you type)' -AsSecureString
$token = [Runtime.InteropServices.Marshal]::PtrToStringAuto([Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure))
if (-not $guild -or -not $token) { Write-Host 'Both are needed.' -ForegroundColor Yellow; Read-Host 'Press Enter to close'; exit 1 }

$env:GUILD_ID = $guild.Trim()
$env:DISCORD_TOKEN = $token.Trim()

Write-Host "`nFirst a dry run, which only prints the plan:`n"
node setup.mjs --dry-run
$go = Read-Host "`nBuild the server now? (y/n)"
if ($go -ne 'y') { Write-Host 'Nothing was changed.'; Read-Host 'Press Enter to close'; exit 0 }

$gh = Read-Host 'Also add the release webhook to the GitHub repo with the gh CLI? (y/n)'
if ($gh -eq 'y') { node setup.mjs --github } else { node setup.mjs }

# the token only lived in this window
Remove-Item Env:\DISCORD_TOKEN, Env:\GUILD_ID -ErrorAction SilentlyContinue
Write-Host "`nDone. Reset the bot token in the developer portal now, and remove the setup bot from the server."
Read-Host 'Press Enter to close'
