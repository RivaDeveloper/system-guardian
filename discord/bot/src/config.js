// Settings come from environment variables (a .env file is read if present, see .env.example). Nothing secret is ever written to disk by the bot.
import { readFileSync, existsSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';

const here = dirname(fileURLToPath(import.meta.url));

function loadDotEnv(file) {
  if (!existsSync(file)) return;
  for (const line of readFileSync(file, 'utf8').split(/\r?\n/)) {
    const m = line.match(/^\s*([A-Z0-9_]+)\s*=\s*(.*?)\s*$/);
    if (m && process.env[m[1]] === undefined) process.env[m[1]] = m[2].replace(/^["']|["']$/g, '');
  }
}
loadDotEnv(join(here, '..', '.env'));

const bool = (v, d) => (v === undefined || v === '' ? d : /^(1|true|yes|on)$/i.test(v));

export function loadConfig(env = process.env) {
  const server = JSON.parse(readFileSync(join(here, '..', '..', 'server.json'), 'utf8'));
  return {
    token: env.DISCORD_TOKEN || '',
    guildId: env.GUILD_ID || '',
    /** log what the bot would do, but delete, timeout and reply to nothing */
    dryRun: bool(env.DRY_RUN, false),
    autoAnswer: bool(env.AUTO_ANSWER, true),
    forumAnswers: bool(env.FORUM_ANSWERS, true),
    dbFile: env.DB_FILE || join(here, '..', 'data', 'bot.db'),
    staffRoles: (env.STAFF_ROLES || 'Owner,Co-owner,Lumen Dev,Lumen Studios Dev,Guardian Dev,Moderator').split(',').map((s) => s.trim()).filter(Boolean),
    /** the channel where every message is treated as a question for the bot */
    askChannel: env.ASK_CHANNEL || 'ask-the-bot',
    modLogChannel: env.MOD_LOG_CHANNEL || 'mod-log',
    /** channels (by name, without the emoji) where the bot may answer a clear question unprompted */
    answerChannels: (env.ANSWER_CHANNELS || 'general,lumen-chat,studios-chat,guardian-chat,android-and-tv,gaming,introductions').split(',').map((s) => s.trim()),
    /** forum channels where the bot greets new posts with likely answers */
    forumChannels: (env.FORUM_CHANNELS || 'help,bug-reports,feature-requests,false-positives').split(',').map((s) => s.trim()),
    /** channels the moderator skips completely */
    ignoreChannels: (env.IGNORE_CHANNELS || 'mod-chat,mod-log,dev-chat').split(',').map((s) => s.trim()),
    allowedInvites: (env.ALLOWED_INVITES || '76PgqgZgjV').split(',').map((s) => s.trim()).filter(Boolean),
    mentionLimit: Number(env.MENTION_LIMIT || 5),
    aiEnabled: !!env.ANTHROPIC_API_KEY,
    server,
  };
}
