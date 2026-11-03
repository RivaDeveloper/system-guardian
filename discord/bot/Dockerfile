# Build from the discord/ folder (the bot reads ../content.mjs and ../server.json):
#   docker build -f bot/Dockerfile -t lumen-bot .
#   docker run -d --restart unless-stopped --env-file bot/.env -v lumen-bot-data:/app/bot/data lumen-bot
FROM node:22-slim
WORKDIR /app
COPY content.mjs server.json ./
WORKDIR /app/bot
COPY bot/package.json bot/package-lock.json ./
RUN npm ci --omit=dev
COPY bot/ ./
ENV DB_FILE=/app/bot/data/bot.db
CMD ["node", "src/index.js"]
