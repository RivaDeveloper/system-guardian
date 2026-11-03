# Lumen Discord server kit

Everything needed to build and run one community server for all three apps: **Lumen** (the streaming app), **Lumen Studios**
(the video editor) and **Guardian** (the antivirus). A setup script creates the whole server, and can also upgrade a server it
built earlier (for example the first, Guardian-only version) in place.

| File | What it is |
|---|---|
| `setup.mjs` | Builds or upgrades the server from `server.json` through Discord's API. Safe to run again. |
| `server.json` | The server's design: roles, channels, permissions, forum tags, first messages, AutoMod. Edit this to change anything. |
| `run-setup.ps1` | A double-click helper that asks for the server ID and bot token and runs the script from the right folder. |
| `assets/` | Server icon, banner, invite splash and 12 custom emoji (drawn from `art.html`), plus an animated icon and banner (`icon-animated.gif`, `banner-animated.gif`, drawn from `art-anim.html`). |
| `render-animated.ps1` | Redraws the two animated GIFs (needs the .NET SDK, Python and ffmpeg). Every frame is a pure function of time, so the loop is seamless. |
| `announcements.md` | Ready-to-post templates: new release, hotfix, beta call, security note, event. |
| `test/` | A fake Discord server and tests, including an upgrade from the old Guardian-only design. |

## What you get

* **Start here:** welcome, rules, announcements (followable), a releases feed for all three apps, roadmap, FAQ.
* **Community:** general, introductions, showcase, what-are-you-watching, gaming, off-topic.
* **One category per app:** *Lumen* (chat, setup help, Android and TV), *Lumen Studios* (chat, a showcase for videos, effects and
  templates) and *Guardian* (chat, threat talk, YARA rules, how it works).
* **Support forums with an app tag:** help, bug reports, feature requests, and Guardian false positives.
* **Development:** dev log, contributing, a Contributors-only dev chat. **Voice:** hangout, watch party, help desk.
* **Staff:** a private area only Lumen Dev and Moderator can see.
* **Safety:** verification level and content filter, three AutoMod rules (mention spam, scam phrases, spam), a rule against
  piracy (Lumen doesn't host or provide any content), and a scam warning pinned in the rules.
* **Roles:** Lumen Dev, Moderator, Contributor, Researcher, Beta Tester. 12 custom emoji, a permanent invite.

## Build a new server (about 15 minutes)

1. **Make the server.** In Discord press **+**, choose *Create My Own*, *For a club or community*.
2. **Make a bot to build it.** <https://discord.com/developers/applications> → *New Application* → **Bot** → *Reset Token* and
   copy the token. Keep it secret: anyone with it controls the bot.
3. **Add the bot.** *OAuth2 → URL Generator*: scope `bot`, permission **Administrator**, open the link and pick your server.
4. **Copy the server ID.** Discord → *Settings → Advanced → Developer Mode* on, right-click the server icon → *Copy Server ID*.
5. **Run it** (needs [Node.js](https://nodejs.org) 18+). Easiest: open this folder and right-click `run-setup.ps1` → *Run with
   PowerShell*. Or in a normal PowerShell window, one line at a time, without the backticks:

   ```powershell
   cd C:\path\to\system-guardian\discord
   $env:DISCORD_TOKEN = "paste the bot token"
   $env:GUILD_ID = "paste the server id"
   node setup.mjs --dry-run
   node setup.mjs
   ```
6. **Release feed.** In Discord: *Server Settings → Integrations → Webhooks → New Webhook*, channel `releases`, copy the URL. Then
   add it to each app's GitHub repo, once per repo (`gh` must be logged in):

   ```powershell
   gh api -X POST repos/OWNER/REPO/hooks -f name=web -F active=true -f "events[]=release" `
     -f "config[url]=PASTE_WEBHOOK_URL/github" -f "config[content_type]=json"
   ```

   Do it for `lumen-releases`, `lumen-studios-releases` and `system-guardian`. Make the webhook yourself: **a webhook created by
   the bot is deleted when the bot is removed**, which silently stops the feed.
7. **Clean up.** *Reset Token* again in the developer portal, then remove the bot. Never commit a token.

The script prints your permanent invite link at the end.

## Upgrade an existing server (the Guardian server becomes Lumen)

Re-add the bot (steps 2 to 4), then run:

```powershell
node setup.mjs --refresh
```

The server is renamed to *Lumen*, `Guardian Dev` becomes `Lumen Dev`, *Security Lab* becomes the *Guardian* category,
`how-it-works` becomes `how-guardian-works`, the new app categories and channels are added, the forums get app tags, and the
bot's earlier welcome, rules, FAQ, roadmap and announcement messages are replaced with the new ones. Everything is matched by
name (or by the earlier name under `"was"` in `server.json`), so nothing is duplicated and your own messages are never touched.
Remove `--refresh` to leave all existing messages alone.

## After the script (only the Discord app can do these)

* **Give yourself Lumen Dev** (Server Settings → Members) and friends Moderator.
* **Onboarding** (Server Settings → Onboarding): ask *"Which app are you here for?"* with answers that open `lumen-chat`,
  `studios-chat` or `guardian-chat`, and offer **Beta Tester** for people who want to test new versions.
* **Welcome screen** (Server Settings → Community): add `welcome`, `help`, `showcase` and `roadmap` with one-line blurbs.
* **Server description:** *Lumen: a glassy streaming app, a video editor and an antivirus, all free for Windows. Help, ideas and
  a look behind the scenes.*
* **Boost perks:** the animated icon needs Boost level 1, the invite splash level 1, a banner level 2 and an *animated* banner
  level 3. The script tries them in that order and keeps whatever the server can have; run it again after boosting, or upload
  `assets/banner-animated.gif` by hand in Server Settings → Overview.

## Running the server

* **Piracy:** it is the main legal risk for a streaming-related server. Delete links, requests and "where can I watch X for free"
  posts, warn once, then time out or ban. Lumen shows what a person's own addons and accounts offer; keep the server away from
  which addons to use for what.
* **False positives matter most for an antivirus.** Triage `false-positives` within a day or two, tag each post, open a GitHub issue
  for real ones, and never ask anyone to upload a file that may hold personal data.
* **Never share live malware.** Delete it, warn the poster, time out repeats. Hashes and names are fine.
* **Scam watch:** staff never DM first (it is in the rules). Ban DM-first accounts quickly.
* **AutoMod alerts** land in `mod-log`. Loosen a rule in Server Settings → AutoMod if it catches normal chat.

## Changing the design

Edit `server.json` and run `node setup.mjs` again. Existing roles, channels and emoji are matched and updated; messages are only
posted into empty channels unless you add `--refresh`. To rename something safely, put its old name under `"was"`. To redraw the
artwork edit `assets/art.html` (it renders `?m=icon`, `banner`, `splash` and `emoji-<name>`).

## Testing the script

```powershell
node test/run-test.mjs
```

starts a fake Discord, builds a server, runs the script again (nothing may be duplicated), runs it with Community unavailable
(forums fall back to text), and upgrades a server built from the old Guardian-only design (`test/guardian-v1.json`) to the current
one. It cannot prove Discord itself accepts every setting, so a look at `--dry-run` first is worthwhile.
