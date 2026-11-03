# Guardian Discord server kit

Everything needed to build and run a proper community server for Guardian: a setup script that creates the whole
server, the artwork, the written content, moderation notes and announcement templates.

| File | What it is |
|---|---|
| `setup.mjs` | Builds the server from `server.json` through Discord's API. Safe to run again. |
| `server.json` | The server's design: roles, channels, permissions, forum tags, first messages, AutoMod. Edit this to change anything. |
| `assets/` | Server icon, banner, invite splash and 8 custom emoji (made from `art.html`). |
| `announcements.md` | Ready-to-post templates: new release, bug fix, beta call, security note, event. |
| `test/` | A fake Discord server and a test that proves the script works and can be run twice without duplicating anything. |

## What you get

**The whole server in one run:** 5 roles (Guardian Dev, Moderator, Contributor, Researcher, Beta Tester), 7 categories and
25 channels, four support **forums** with tags (help, false positives, bug reports, feature requests), a read-only
information area (welcome, rules, announcements, releases, roadmap, FAQ, how it works), community and gaming channels, a
Security Lab for detection talk, a staff area nobody else can see, voice channels, three AutoMod rules (mention spam,
scam phrases, spam), custom emoji, a permanent invite, and a **webhook that posts every new GitHub release** into
the releases channel.

## Set it up (about 15 minutes)

1. **Make the server.** In Discord press **+**, choose *Create My Own*, *For a club or community*, name it Guardian.
   You are the owner, which means you can always do everything.
2. **Make a bot to build it.** Go to <https://discord.com/developers/applications>, *New Application*, name it
   "Guardian Setup". Open **Bot**, press *Reset Token* and copy the token. Keep it secret: anyone with it controls the bot.
3. **Add the bot to your server.** *OAuth2 → URL Generator*: tick scope `bot`, tick permission **Administrator**, open the
   generated link and pick your server.
4. **Copy the server ID.** Discord → *Settings → Advanced → Developer Mode* on, then right-click the server icon →
   *Copy Server ID*.
5. **Run the script** (needs [Node.js](https://nodejs.org) 18 or newer). In PowerShell, from this folder:

   ```powershell
   $env:DISCORD_TOKEN = "paste the bot token"
   $env:GUILD_ID = "paste the server id"
   node setup.mjs --dry-run      # shows the plan, sends nothing
   node setup.mjs --github       # builds the server and adds the release webhook to the GitHub repo through the gh CLI
   ```

   Leave out `--github` and the script prints the webhook URL for you to add in GitHub (*Settings → Webhooks*, payload
   URL ending in `/github`, content type `application/json`, only the *Releases* event).
6. **Clean up.** In the developer portal press *Reset Token* again and remove the "Guardian Setup" bot. Never commit a token.

   > **The release webhook belongs to the bot.** Discord deletes a bot's webhooks when the bot is removed, which stops the
   > release feed. Before you remove the bot, make your own webhook: *Server Settings → Integrations → Webhooks → New
   > Webhook*, pick the `releases` channel, copy the URL, and point GitHub at it with
   > `gh api -X PATCH repos/<owner>/<repo>/hooks/<hook id> -f "config[url]=<webhook url>/github" -f "config[content_type]=json"`
   > (or edit the webhook in the repo's *Settings → Webhooks*). A webhook you create yourself survives.

The script prints your permanent invite link at the end. Send that link to people.

## After the script (only the Discord app can do these)

* **Give yourself the Guardian Dev role** (Server Settings → Members). Give friends Moderator.
* **Onboarding** (Server Settings → Onboarding): a good first question is *"What do you want to do here?"* with answers
  *Get help* → opens `help`, *Test new versions* → gives **Beta Tester**, *Talk security* → gives **Researcher**.
* **Rules screening** is on once Community is enabled: members tick the rules before they can talk.
* **Welcome screen** (Server Settings → Community): add `welcome`, `help`, `showcase`, `roadmap` with one-line blurbs.
* **Server description** (paste into Server Settings → Overview or Discovery):
  > Guardian is a free antivirus and PC-care app for Windows, with its own scan engine, real-time protection and a switch
  > for every shield. Help, false-positive reports, ideas and a look at how a Windows antivirus is made.
* **Pin a short "how to report a false positive"** in the forum: the forum guidelines (the channel topic) already say it.
* **Follow the announcements channel** from your Lumen server if you run one, so Guardian news reaches those people too.
* **Boost perks:** the script already tries to set the banner and invite splash. They need Boost levels 2 and 1; run the
  script again after boosting and they are applied. Until then the images in `assets/` are ready to upload by hand.

## Running the server

* **False positives matter most for an antivirus.** Triage every post in `false-positives` within a day or two: tag it
  *Needs more info*, *Fixed* or *Not a false positive*, and open a GitHub issue for real ones. Never ask anyone to upload
  a file that may contain personal data.
* **Never share live malware.** Delete it, warn the poster, and use a timeout for repeats. Hashes and names are fine.
* **Scam watch:** the staff will never DM first. Pin that (it is in the rules) and ban DM-first accounts quickly.
* **AutoMod alerts** land in `mod-log`. If a rule catches legitimate chat, loosen it in Server Settings → AutoMod.
* **Ship notes:** the release webhook posts automatically; add a human post from `announcements.md` when a release matters.

## Changing the design

Edit `server.json`, then run `node setup.mjs` again. Existing roles, channels and emoji are matched by name and updated;
messages are only posted into channels that are still empty, so your own posts are never overwritten. To rebuild a
message, delete it in Discord and run the script again.

To redraw the artwork edit `assets/art.html` (it renders `?m=icon`, `banner`, `splash` and `emoji-<name>`).

## Testing the script

```powershell
node test/run-test.mjs
```

starts a fake Discord, builds a server on it, runs the script a second time (nothing may be duplicated) and a third time
with Community unavailable (forums must fall back to text channels). It cannot prove that Discord itself will accept every
setting, which is why `--dry-run` and a fresh, empty test server are worth a look before you use your real one.
