#!/usr/bin/env bash
# One-shot setup of the Lumen Helper bot on a fresh Ubuntu VM (tested target: Oracle Cloud Always Free, Ubuntu 22.04/24.04).
# Run on the VM as the normal user (ubuntu):   bash oracle-setup.sh
# It asks for the bot token and server id, so nothing secret is ever typed into a chat or stored in the repo.
set -euo pipefail

REPO=https://github.com/RivaDeveloper/system-guardian.git
APP=$HOME/system-guardian
BOT=$APP/discord/bot

echo "== 1/6 packages"
sudo apt-get update -y
sudo apt-get install -y git curl ca-certificates

echo "== 2/6 swap (the free x86 VM has 1 GB of RAM)"
if ! swapon --show | grep -q .; then
  sudo fallocate -l 1G /swapfile && sudo chmod 600 /swapfile && sudo mkswap /swapfile && sudo swapon /swapfile
  grep -q '/swapfile' /etc/fstab || echo '/swapfile none swap sw 0 0' | sudo tee -a /etc/fstab >/dev/null
fi

echo "== 3/6 Node.js 22"
if ! command -v node >/dev/null || [ "$(node -p 'process.versions.node.split(".")[0]')" -lt 22 ]; then
  curl -fsSL https://deb.nodesource.com/setup_22.x | sudo -E bash -
  sudo apt-get install -y nodejs
fi
node -v

echo "== 4/6 the bot"
if [ -d "$APP/.git" ]; then git -C "$APP" pull --ff-only; else git clone --depth 1 "$REPO" "$APP"; fi
cd "$BOT"
npm ci --omit=dev
mkdir -p data

echo "== 5/6 settings"
if [ ! -f "$BOT/.env" ]; then
  read -r -p "Server (guild) id: " GUILD_ID
  read -r -s -p "Bot token (hidden): " TOKEN; echo
  read -r -p "Start in dry-run mode (logs only, no deleting/answering)? [Y/n]: " DRY
  case "${DRY:-Y}" in n|N) DRY_RUN=0 ;; *) DRY_RUN=1 ;; esac
  umask 077
  printf 'DISCORD_TOKEN=%s\nGUILD_ID=%s\nDRY_RUN=%s\nDB_FILE=%s/data/bot.db\n' "$TOKEN" "$GUILD_ID" "$DRY_RUN" "$BOT" > "$BOT/.env"
  unset TOKEN
else
  echo ".env already exists, keeping it."
fi

echo "== 6/6 service (starts at boot, restarts if it crashes)"
sudo tee /etc/systemd/system/lumen-bot.service >/dev/null <<EOF
[Unit]
Description=Lumen Helper Discord bot
After=network-online.target
Wants=network-online.target

[Service]
User=$USER
WorkingDirectory=$BOT
EnvironmentFile=$BOT/.env
ExecStart=$(command -v node) src/index.js
Restart=always
RestartSec=10

[Install]
WantedBy=multi-user.target
EOF
sudo systemctl daemon-reload
sudo systemctl enable --now lumen-bot
sleep 5
sudo systemctl --no-pager status lumen-bot | head -n 12
echo
echo "Done. Logs:   journalctl -u lumen-bot -f"
echo "Update later: cd $APP && git pull && cd discord/bot && npm ci --omit=dev && sudo systemctl restart lumen-bot"
echo "Go live:      edit $BOT/.env (DRY_RUN=0) then: sudo systemctl restart lumen-bot"
