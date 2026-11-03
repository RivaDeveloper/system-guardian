# Optional: starts the bot by itself whenever you sign in to Windows (a scheduled task for your user, no administrator needed).
#   powershell -ExecutionPolicy Bypass -File .\install-autostart.ps1          install
#   powershell -ExecutionPolicy Bypass -File .\install-autostart.ps1 -Remove  uninstall
param([switch]$Remove)
$name = 'LumenServerBot'
if ($Remove) { Unregister-ScheduledTask -TaskName $name -Confirm:$false -ErrorAction SilentlyContinue; 'Removed.'; exit }
$node = (Get-Command node -ErrorAction Stop).Source
$action = New-ScheduledTaskAction -Execute $node -Argument 'src/index.js' -WorkingDirectory $PSScriptRoot
$trigger = New-ScheduledTaskTrigger -AtLogOn -User $env:USERNAME
$settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -RestartCount 5 -RestartInterval (New-TimeSpan -Minutes 1) -ExecutionTimeLimit (New-TimeSpan -Days 365)
Register-ScheduledTask -TaskName $name -Action $action -Trigger $trigger -Settings $settings -Description 'Moderation and Q&A bot for the Lumen Discord server' -Force | Out-Null
"Installed. The bot will start when you sign in. Start it now with: Start-ScheduledTask -TaskName $name"
