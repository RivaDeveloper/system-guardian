# Lumen server bot

A moderator and helper for the Lumen Discord server (Lumen, Lumen Studios, Guardian). It is a normal Discord bot written in Node.js:
it keeps the server tidy, and it answers questions using the same guides people can read in the channels.

## What it does

**Moderation** (staff are never touched; every action is written to `#mod-log`)

| It catches | What happens |
|---|---|
| **Keys and tokens** (Discord, GitHub, AI keys, Real-Debrid style keys, addon links with a key inside) | Message removed, the member is told to create a new key. The key itself is never logged or stored. |
| **Scams** (free nitro, gift links, airdrops) | Removed, timed out for a day, a moderator is pinged. Usually a hacked account. |
| **Piracy** (magnet links, piracy sites, cracks, keygens) | Removed, strike (rule 3). A question like "is Lumen a pirate site?" is left alone. |
| **Programs and archives as files** (`.exe`, `.zip`, `.msi`, `.ps1`, `.apk` ...) | Removed with an explanation: share a hash or the official GitHub link instead. No strike. This is an antivirus server. |
| **Invites, mention spam, floods, repeats**, and links from brand-new members | Removed, strike (new-member links: no strike). |
| **Join bursts** (8 joins in a minute) | A heads-up to the moderators. The bot never acts on this by itself. |

Strikes last 30 days: 1st a warning, 2nd a 10 minute timeout, 3rd an hour, 4th a day (and a moderator is asked), more a week.
**The bot never bans and never kicks.** Those stay human decisions.

**Answering questions**
* `/ask <question>` and **@mentioning the bot** answer from the server's guides (FAQ, how-it-works, setup guide, roadmap and 24 extra Q&As in `kb-extra.json`).
* A **greeting under every new forum post** (help, bug reports, feature requests, false positives): a likely answer if there is one, plus what a good report should include.
* In chat channels it **speaks up on its own only when sure**: a clear question, a strong match, at most once every 90 seconds per channel and once every 10 minutes per person. Otherwise it stays quiet.
* Optional **AI mode**: with an `ANTHROPIC_API_KEY` the answer is phrased by Claude, but only from the guide passages the search found (it must say "I'm not sure" otherwise, never helps with piracy, never asks for keys). 6 AI answers per person per hour, and any failure falls back to the plain guide answer. Without a key everything still works and costs nothing.

**Slash commands for staff:** `/warn`, `/warnings`, `/clearwarnings`, `/timeout`, `/purge`, `/slowmode`.

## Set it up

1. **Make a bot for it.** <https://discord.com/developers/applications> → *New Application* → name it *Lumen Helper*. (Use a new
   application rather than the setup bot: this one needs far fewer permissions.)
2. **Turn on the privileged intents.** *Bot* → *Privileged Gateway Intents* → switch on **Message Content Intent** and **Server
   Members Intent**, and save. Without them Discord refuses the login.
3. **Get the token.** *Bot* → *Reset Token*, copy it. Keep it secret.
4. **Invite it.** Open this link after putting your application's ID (*General Information*) in place of `APPLICATION_ID`:

   `https://discord.com/oauth2/authorize?client_id=APPLICATION_ID&scope=bot%20applications.commands&permissions=1391569497168`

   That permission set is: view channels, send messages (also in threads), embed links, read history, add reactions, manage
   messages, manage threads, manage channels (for `/slowmode`) and time out members. Nothing else.
5. **Put the bot's role above your members' roles** (Server Settings → Roles), so it can time them out and delete their messages. Keep it
   below the Moderator and Lumen Dev roles.
6. **Configure.** Copy `.env.example` to `.env` and fill in `DISCORD_TOKEN` and `GUILD_ID`. It starts with `DRY_RUN=1`.
7. **Check, then dry-run.**

   ```powershell
   npm install
   npm start -- --check     # logs in, shows what it can see and do, posts nothing
   npm start                # dry run: look at what it logs while people chat
   ```
8. **Go live.** Type `/bot mode live` in Discord (admins only), or set `DRY_RUN=0` in `.env` and restart. `/bot mode test` switches back, `/bot chat on|off` controls unprompted answers and `/bot status` shows both. These choices are saved in `data/settings.json` and win over `.env`.

## Keep it running

A bot only works while it is running. Pick one:

* **Your PC:** `run-bot.ps1` runs it in a window; `install-autostart.ps1` starts it every time you sign in to Windows. The bot is offline when
  the PC is off.
* **Any always-on machine** (a spare PC, a Raspberry Pi, a small rented server): `npm start`, or Docker:

  ```bash
  # from the discord/ folder
  docker build -f bot/Dockerfile -t lumen-bot .
  docker run -d --restart unless-stopped --env-file bot/.env -v lumen-bot-data:/app/bot/data lumen-bot
  ```

Moderation and answers pause while it is offline; nothing breaks. The server's own AutoMod rules (set up by `setup.mjs`) keep working
without the bot.

## Tuning

Everything is an environment variable (see `.env.example`): which channels the bot may answer in, the staff roles, allowed invite
codes, the mention limit, AI on or off. The rules themselves are plain functions in `src/rules.js`, and every one is covered by a test.

## Keeping the answers current

The bot reads `../content.mjs` (the same text posted in the channels) and `kb-extra.json`. After you change either, restart the bot.
If it answers something wrongly, add a better entry to `kb-extra.json` with the question phrased the way people ask it.

## What it stores

A small SQLite file (`data/bot.db`) with strikes only: user id, rule, reason, date. No message text, no keys. `/clearwarnings` empties a member's strikes.

## Tests

```powershell
npm test
```

23 tests cover the rules with real-looking messages (including things that must **not** be caught), strike escalation, the
answer search on real questions (and silence on unrelated ones), the AI path with a fake API (prompt, rate limit, fallback), and the
moderator with fake Discord objects (staff skipped, dry run, escalation, a leaked key never reaching the log). They can't prove how
Discord itself behaves, which is why the bot starts in dry-run mode.
