// The words and pictures that setup.mjs posts into the server. Strings can use:
//   {linkName}      a link from "links" in server.json            <#{channelKey}>  a channel mention
//   :emojiName:     one of the server's custom emoji (lumen, studios, guardian, tv, play, protected, ...)
// An entry is either one embed, or { embeds: [...] } to post several embeds in one message. Embeds that share the same
// "url" are shown by Discord as a picture gallery. Pictures come from the three websites and from this repo's assets folder.
// Facts here come from the websites themselves (getlumen.pages.dev, lumen-studios..., guardian-antivirus...).

const RED = 16723541, VIOLET = 9137151, CYAN = 3723519, GREEN = 3996804, GOLD = 16756810;

// a gallery: the first embed carries the text, the rest are just pictures
const gallery = (url, color, first, images) => ({
  embeds: images.map((image, i) => (i === 0 ? { ...first, url, color, image } : { url, color, image })),
});

export const messages = {
  // ------------------------------------------------------------------------------------------------ start here
  welcome: [
    {
      embeds: [
        { color: RED, image: '{assetBase}/banner.png' },
        {
          color: RED, title: 'Lumen', url: '{lumenSite}', thumbnail: '{assetBase}/emoji-lumen.png', image: '{assetBase}/lumen/home.jpg',
          description: ':lumen: **The streaming app.** Your Stremio addons and Real-Debrid account on one glass screen: movies, series and anime, a proper player, profiles, calendars and Watch together. Windows, Android and Samsung TV. Lumen doesn\'t host or provide any content.\n\n**[Download for Windows]({lumenDownload})** · [Website]({lumenSite}) · <#{lumenchat}>',
        },
        {
          color: VIOLET, title: 'Lumen Studios', url: '{studiosSite}', thumbnail: '{assetBase}/emoji-studios.png', image: '{studiosMedia}/editor.jpg',
          description: ':studios: **The video editor.** A multi-track timeline, text, stickers, 75+ effects, 94 transitions, filters, automatic captions and sharp MP4 export. No account, no watermark.\n\n**[Download]({studiosDownload})** · [Website]({studiosSite}) · <#{studioschat}>',
        },
        {
          color: CYAN, title: 'Guardian', url: '{guardianSite}', thumbnail: '{assetBase}/emoji-guardian.png', image: '{guardianMedia}/protection.jpg',
          description: ':guardian: **The antivirus.** Its own scan engine, real-time protection that even checks programs as they start, and a switch for every shield. Free for Windows 10 and 11.\n\n**[Download]({guardianDownload})** · [Website]({guardianSite}) · <#{guardianchat}>',
        },
      ],
    },
    {
      embeds: [
        {
          color: RED,
          fields: [
            { name: ':play: New here?', value: '1. Read the <#{rules}>\n2. Say hi in <#{introductions}>\n3. New to Lumen? Read <#{lumenhow}>, then set it up with <#{lumenguide}>\n4. Chat about your app in <#{lumenchat}>, <#{studioschat}> or <#{guardianchat}>\n5. Stuck? Make a post in <#{help}> and pick the app in the tags' },
            { name: ':scan: Find your way around', value: '<#{announcements}> news · <#{releases}> new versions of all three apps\n<#{help}> help · <#{bugs}> bugs · <#{features}> ideas · <#{falsepositives}> Guardian false positives\n<#{showcase}> and <#{studiosshowcase}> for what you make · <#{faq}> quick answers' },
            { name: ':alert: Good to know', value: 'The installers aren\'t code-signed yet, so Windows SmartScreen may warn you the first time: choose *More info*, then *Run anyway*. All three apps update themselves.' },
          ],
          footer: 'Lumen · made by one developer',
        },
      ],
    },
  ],

  rules: [
    {
      title: 'Server rules', color: RED, thumbnail: '{assetBase}/emoji-protected.png',
      description: '**1. Be decent.** No harassment, hate or personal attacks. Disagree with ideas, not people.\n\n**2. Use the right channel.** Support goes in <#{help}>, bugs in <#{bugs}>, ideas in <#{features}>. Pick the app in the tags.\n\n**3. No piracy.** No links, no requests, no help finding free copies of films, shows, games or software, and no talk of cracked apps. Lumen doesn\'t host or provide any content and isn\'t a way around paying for it.\n\n**4. No malware, cracks, keygens or cheats.** Not as files, not as links. Talk about samples with hashes and names only.\n\n**5. Defensive security only.** Discuss how to detect and stop threats, not how to attack other people.\n\n**6. Protect your privacy.** Never post passwords, tokens, API keys (Real-Debrid, VirusTotal or any other) or personal files. Never upload documents, photos or ID scans when reporting a problem.\n\n**7. No spam and no self-promotion** without asking staff first. No unsolicited invites or DMs.\n\n**8. Keep it in English** in the main channels so everyone can follow.\n\n**9. Follow the [Discord Terms](https://discord.com/terms) and [Guidelines](https://discord.com/guidelines).**\n\n**10. Staff have the final say.** If you think a decision was wrong, ask a moderator politely in private.',
      fields: [
        { name: ':alert: Scam warning', value: 'Staff will **never** DM you first, ask for a password or API key, or ask you to download something outside the official GitHub releases ([Lumen]({lumenRepo}), [Lumen Studios]({studiosRepo}), [Guardian]({guardianRepo})). If someone does, report them to a moderator.' },
      ],
    },
  ],

  announcements: [
    {
      embeds: [
        { color: RED, image: '{assetBase}/banner.png' },
        {
          color: RED, title: 'Welcome to the Lumen server', thumbnail: '{assetBase}/emoji-lumen.png',
          description: 'This is the home for :lumen: **Lumen** (streaming), :studios: **Lumen Studios** (video editor) and :guardian: **Guardian** (antivirus): help, ideas, bug reports and a look behind the scenes.\n\nNew versions of all three apps are posted in <#{releases}>. If you only read one thing, read the <#{rules}>.',
        },
      ],
    },
  ],

  releasesinfo: [
    { title: 'New versions land here', color: GREEN, thumbnail: '{assetBase}/emoji-play.png', description: 'Every GitHub release of Lumen, Lumen Studios and Guardian is posted in this channel automatically, with its notes. The apps update themselves, so you usually don\'t have to do anything.' },
  ],

  faq: [
    {
      title: 'FAQ: Lumen (streaming)', color: RED, thumbnail: '{assetBase}/emoji-lumen.png',
      fields: [
        { name: 'What is Lumen?', value: 'An app that turns the Stremio addons and Real-Debrid account you already have into one home for movies, series and anime, on Windows, Android and Samsung TV.' },
        { name: 'Does it come with movies or shows?', value: 'No. Lumen only plays what the addons you add can find. You choose the addons and you\'re responsible for how you use them. Lumen doesn\'t host or provide any content.' },
        { name: 'Is it free? Do I need an account?', value: 'Free on every device, with no ads. Real-Debrid and some addons are separate services with their own prices. No account is needed; a free Lumen account is optional and keeps your profiles, lists and addons the same on every PC, phone and TV.' },
        { name: 'Windows says "Windows protected your PC"', value: 'The installer isn\'t code-signed yet. Choose **More info**, then **Run anyway**. Installers are published on GitHub with every release.' },
        { name: 'Which file should I download?', value: 'Most PCs need `Lumen-Setup-x.y.z.exe`. Laptops with a Snapdragon (ARM) chip need the `-arm64` one. There is also a portable version that runs without installing (it doesn\'t update itself).' },
        { name: 'Mac, Linux or iPhone?', value: 'Not at the moment. Lumen is for Windows 10 and 11, Android phones and Samsung smart TVs, with a companion channel for Roku TVs.' },
      ],
    },
    {
      title: 'FAQ: Lumen Studios (video editor)', color: VIOLET, thumbnail: '{assetBase}/emoji-studios.png',
      fields: [
        { name: 'Is it free?', value: 'Yes. No ads, no account and no watermark on your videos.' },
        { name: 'Does it need the internet?', value: 'Editing and exporting work offline; it only goes online to look for updates. The speech model for automatic captions (148 MB) downloads the first time you use that feature, then runs on your PC.' },
        { name: 'What can I import and export?', value: 'Most common video, photo and audio files import. Export is MP4 (H.264 and AAC). Projects save as `.vedit` files and captions as `.srt`.' },
        { name: 'How do updates work?', value: 'It checks for new versions itself and downloads them in the background. A **Restart to update** button appears at the top; if you ignore it, the update installs the next time you close the app.' },
        { name: 'Mac or Linux?', value: 'Not yet. Lumen Studios is for Windows 10 and 11 (64-bit).' },
      ],
    },
    {
      title: 'FAQ: Guardian (antivirus)', color: CYAN, thumbnail: '{assetBase}/emoji-guardian.png',
      fields: [
        { name: 'Do I need Windows Defender?', value: 'No. Real-time protection, the quick, folder and deep scans, the hash lists and the signature rules all work without it. If Defender is on, the two work side by side.' },
        { name: 'Is it a kernel-level antivirus?', value: 'No. Guardian has no kernel driver. With administrator rights it listens to Windows\' own kernel process events, so it sees every program within moments of it starting and can pause a risky one, but it can\'t stop a program before its first instruction. That needs a Microsoft-signed driver and a paid certificate.' },
        { name: 'Does it upload my files?', value: 'Never. The optional VirusTotal check sends only a file\'s SHA-256 fingerprint, and only if you turn it on and add your own key.' },
        { name: 'Can it delete a good file by mistake?', value: 'It never deletes. Quarantine moves a file aside and you can restore it. "It\'s fine" stops a file being raised again.' },
        { name: 'Where are its files?', value: 'Settings, history and quarantine live in `%LOCALAPPDATA%\\Guardian`. If something breaks, the end of `errors.log` in that folder is what we need.' },
      ],
    },
  ],

  roadmap: [
    {
      title: 'What the apps do today', color: GREEN,
      fields: [
        { name: ':lumen: Lumen', value: 'A home screen built from your addons with Continue watching and a Top 10, a player that fixes Dolby and DTS sound, Watch together, profiles with a kids mode behind a PIN, anime and series calendars, 15 themes, 5 languages, Play on TV, Discord presence, and apps for Windows, Android and Samsung TV.' },
        { name: ':studios: Lumen Studios', value: 'A multi-track timeline, text and stickers, 75+ effects, 94 transitions, filters, automatic captions, 32 templates, HDR tone mapping and frame-accurate MP4 export.' },
        { name: ':guardian: Guardian', value: 'Its own scan engine (quick, folder and deep scans), real-time protection that also checks programs as they start, 9,000+ known-bad hashes and 5,000+ YARA rules, plus registry repair, cleanup, drivers, debloat and a safe BIOS update assistant.' },
      ],
    },
    {
      title: 'Ideas on the table', color: GOLD,
      description: 'These are ideas people ask for. They are **not promises** and have no dates.',
      fields: [
        { name: 'Code-signed installers', value: 'Would remove the SmartScreen warning. Needs a paid certificate.' },
        { name: 'Mac and Linux versions', value: 'Asked for often. The apps would need new interfaces and parts rewritten.' },
        { name: 'A Guardian kernel driver', value: 'Would let Guardian block programs before they start. Needs a Microsoft-signed driver, which needs a paid certificate.' },
        { name: 'Your idea', value: 'Post it in <#{features}> and pick the app.' },
      ],
    },
  ],

  // ------------------------------------------------------------------------------------------------ community
  general: [
    {
      title: 'Welcome to the main room', color: RED, thumbnail: '{assetBase}/emoji-lumen.png',
      description: 'Anything about the Lumen apps, and anything else that fits the <#{rules}>.',
      fields: [
        { name: 'Conversation starters', value: '• Which app are you using, and on which device?\n• What is the one setting you changed first?\n• What would make your day-to-day with it better?' },
        { name: 'Where things go', value: '<#{lumenchat}> Lumen · <#{studioschat}> Lumen Studios · <#{guardianchat}> Guardian\n<#{help}> for problems · <#{features}> for ideas · <#{offtopic}> for everything else' },
      ],
    },
  ],

  introductions: [
    {
      title: 'Introduce yourself', color: RED, thumbnail: '{assetBase}/emoji-play.png',
      description: 'A line or two is plenty. A template if you want one:',
      fields: [
        { name: 'Copy this', value: '```\nName / nickname:\nApps I use: Lumen / Lumen Studios / Guardian\nMy device: Windows PC / Android / Samsung TV\nI am here to: get help / test / chat / learn about security\n```' },
      ],
    },
  ],

  showcase: [
    {
      embeds: [
        {
          title: 'Show off your setup', color: RED, url: '{lumenSite}', thumbnail: '{assetBase}/emoji-lumen.png', image: '{assetBase}/lumen/home-rows.jpg',
          description: 'Post screenshots of your Lumen home screen, your themes and profiles, your cleanest PCs, your Guardian scans, anything you are proud of. One post per picture, a line about it, and please crop out personal details.\n\n**For videos made with Lumen Studios** use <#{studiosshowcase}>.\n\nHere is what the three apps look like out of the box:',
        },
        { url: '{lumenSite}', color: RED, image: '{assetBase}/lumen/anime-calendar.jpg' },
        { url: '{lumenSite}', color: RED, image: '{assetBase}/lumen/themes.jpg' },
        { url: '{lumenSite}', color: RED, image: '{assetBase}/lumen/series.jpg' },
      ],
    },
    gallery('{guardianSite}', CYAN, { title: 'Guardian' }, ['{guardianMedia}/overview.jpg', '{guardianMedia}/protection.jpg', '{guardianMedia}/tools.jpg']),
  ],

  watching: [
    {
      title: 'What are you watching?', color: RED, thumbnail: '{assetBase}/emoji-play.png',
      description: 'Talk about shows, films and anime: what you are on, what you finished, what you would recommend.\n\n**No piracy.** No links, no requests, and no help finding free copies. That is in the <#{rules}> and it is how this server stays here.',
      fields: [
        { name: 'A good post', value: '```\nWatching: title (season/episode)\nWhat I think so far:\nSpoilers? put them in ||spoiler tags||\n```' },
        { name: 'Free to watch, legally', value: 'The film in the Lumen screenshots is **Sintel**, an open film by the Blender Foundation under a Creative Commons licence. The Blender open movies (Big Buck Bunny, Sintel, Tears of Steel and others) are made to be shared.' },
      ],
    },
  ],

  gaming: [
    {
      title: 'Gaming and a fast, safe PC', color: GREEN, thumbnail: '{assetBase}/emoji-guardian.png', image: '{guardianMedia}/tools.jpg',
      description: 'Games, anti-cheat, performance and keeping a gaming PC safe. No cheats, no cracks: see the <#{rules}>.',
      fields: [
        { name: 'Guardian is anti-cheat aware', value: 'Optional memory scanning refuses to run while EasyAntiCheat, BattlEye, Vanguard or similar is running, so it can\'t put your account at risk. The real-time watcher runs at low priority.' },
        { name: 'Performance tweaks', value: 'Guardian\'s **System tools → Performance tweaks** has safe, reversible settings for a faster PC (power plan, Game Mode and more), each switched on or off one at a time. It deliberately never offers to stop Defender or the firewall.' },
        { name: 'Talk here about', value: 'Your settings, frame-rate wins, launcher trouble, which anti-cheat does what, and how you keep a gaming PC clean.' },
      ],
    },
  ],

  // ------------------------------------------------------------------------------------------------ Lumen
  lumenchat: [
    gallery('{lumenSite}', RED, {
      title: 'Lumen: every story, in a new light', thumbnail: '{assetBase}/emoji-lumen.png',
      description: ':lumen: Lumen turns the Stremio addons and Real-Debrid account you already have into one beautiful home for movies, series and anime, on your PC, your phone and your TV. Free, no ads, no account needed, and it updates itself.\n\n**[Download for Windows]({lumenDownload})** · [Website]({lumenSite}) · <#{lumensetup}> to get started',
    }, ['{assetBase}/lumen/home.jpg', '{assetBase}/lumen/detail.jpg', '{assetBase}/lumen/anime.jpg', '{assetBase}/lumen/anime-calendar.jpg']),
    {
      title: 'What it does', color: RED,
      fields: [
        { name: 'Your addons, beautifully', value: 'Reads your Stremio addons and builds a real home screen: a featured title, Continue watching, a glowing Top 10 and rows tinted by whatever you are looking at.' },
        { name: 'Every source, ranked', value: 'Cast, trailers and every stream your addons find, sorted by quality, with your usual pick marked and a warning when a sound format needs fixing.' },
        { name: 'A player that just works', value: 'Fixes Dolby and DTS sound your PC can\'t play as it plays. Pick audio languages, style subtitles, skip intros and line up the next episode.' },
        { name: 'Anime, done properly', value: 'A dedicated tab with its own Top 10, a weekly airing calendar in your time zone and your MyAnimeList or AniList progress.' },
        { name: 'Watch together', value: 'Share a 6-letter code and watch in sync with friends. Play, pause and jumps are shared, while everyone streams from their own sources.' },
        { name: 'Shows on Discord', value: 'Let friends see what you are watching, with the title, episode and a progress timer. Off for kids profiles, and one setting turns it off.' },
      ],
    },
    gallery('{lumenSite}', RED, {
      title: 'Small things, done right',
      description: '**Library:** lists, history and anime progress synced with MyAnimeList or AniList, with new-episode badges.\n**Languages:** audio tracks and subtitles in one menu, with subtitle size, colour and timing.\n**Profiles & kids:** their own lists and look; kids see only titles rated for their age, behind a PIN.\n**15 themes** per profile · **Skip intro** on the progress bar · **Calendars** for series and movies · **One search** across every addon.',
    }, ['{assetBase}/lumen/themes.jpg', '{assetBase}/lumen/search.jpg', '{assetBase}/lumen/series.jpg', '{assetBase}/lumen/movies.jpg']),
  ],

  lumensetup: [
    gallery('{lumenSite}/windows/', RED, {
      title: 'Up and running in a minute', thumbnail: '{assetBase}/emoji-lumen.png',
      description: '**1. Download & install.** Run the installer. Windows may say "Windows protected your PC" because Lumen isn\'t code-signed yet: choose *More info*, then *Run anyway*.\n\n**2. Add your addons.** Open *Addons › Get addons* and add the ones you like, or paste the links of addons you already use.\n\n**3. Link Real-Debrid.** Optional: link your account in *Settings* for cached torrent sources, then press play.\n\n**[Download Lumen]({lumenDownload})**',
    }, ['{assetBase}/lumen/realdebrid.jpg', '{assetBase}/lumen/playback.jpg']),
    {
      title: 'Good to know', color: RED,
      fields: [
        { name: 'Which file?', value: 'The Download button on the website picks for you. Most PCs need `Lumen-Setup-x.y.z.exe`; Snapdragon (ARM) laptops need the `-arm64` one; a portable version runs without installing but doesn\'t update itself.' },
        { name: 'Real-Debrid and addons', value: 'These are separate services with their own prices. Your Real-Debrid key stays on your device. Lumen only plays what the addons you add can find, and you are responsible for how you use them.' },
        { name: 'Sign-in is optional', value: 'A free Lumen account keeps your profiles, lists and addons the same on every PC, phone and TV. Your addons are encrypted with your PIN before they leave your device.' },
        { name: 'Updates', value: 'Lumen downloads new versions in the background. When one is ready, Settings shows **Restart to update**.' },
        { name: 'Stuck?', value: 'Make a post in <#{help}> with the Lumen tag: your Lumen version, what you did and the exact message you saw.' },
      ],
    },
  ],

  lumendevices: [
    gallery('{lumenSite}/android/', RED, {
      title: 'Lumen for Android', thumbnail: '{assetBase}/emoji-play.png',
      description: ':lumen: Everything you love about Lumen on the PC, made for your thumb: a tab bar, big touch controls in the player, and your profiles, lists and addons the moment you sign in. Android 8 and newer, about 7 MB.\n\n**Install:** Lumen isn\'t on Google Play. Open the Lumen releases page on your phone, download the APK, open it, allow your browser to install apps, tap *Install*, then sign in or add addons. The APK is signed with the same key every time, so Android only accepts updates that really come from Lumen.\n\n**Different from the PC:** the phone uses Android\'s own player. The PC\'s sound fix, clips and Play on TV aren\'t on phones, and sources with only DTS sound are marked when your phone can\'t play them.\n\n**[Get the APK]({lumenDownload})**',
    }, ['{lumenMedia}/phone-home.jpg', '{lumenMedia}/phone-detail.jpg', '{lumenMedia}/phone-episodes.jpg', '{lumenMedia}/phone-anime.jpg']),
    gallery('{lumenSite}/tv/', RED, {
      title: 'Lumen for Samsung TV', thumbnail: '{assetBase}/emoji-tv.png',
      description: ':tv: Lumen on your living-room TV, made for the remote. Samsung\'s own player plays 4K, HDR and Dolby, and signing in with a QR code on your phone brings your profiles and addons in seconds. For Samsung Tizen TVs from 2019 on.\n\n**Install:** it isn\'t in Samsung\'s app store. You sign it once on a PC: turn on Developer mode on the TV (Apps, then type 1 2 3 4 5), install Samsung\'s free Tizen Studio, create a free Samsung TV certificate, sign the Lumen file from the TV release, install it, and sign in with the QR code. The TV release page lists every step.\n\n**The remote:** arrows move, OK opens or pauses, Back goes back, left and right skip 10 seconds while playing, media keys just work.\n\n**Different from the PC:** Samsung TVs have no DTS, so DTS-only sources are marked. Google and MyAnimeList sign-in, addon setup pages, clips and Play on TV are set up on the PC.\n\n**Roku TV?** Lumen for Windows can send what you are watching to the Lumen channel on Roku: choose *Play on TV* in the player.',
    }, ['{lumenMedia}/tv-home.jpg', '{lumenMedia}/tv-detail.jpg', '{lumenMedia}/tv-anime.jpg', '{lumenMedia}/tv-rows.jpg']),
  ],



  askbot: [
    {
      title: 'Ask the bot', color: VIOLET, thumbnail: '{assetBase}/emoji-lumen.png',
      description: 'Type your question here in plain words and the **Lumen Helper** bot answers from the server guides, usually in a few seconds. It covers :lumen: Lumen, :studios: Lumen Studios and :guardian: Guardian.',
      fields: [
        { name: 'Good questions', value: '• How do I link Real-Debrid in Lumen?\n• Do I need Windows Defender for Guardian?\n• Does Lumen Studios put a watermark on my videos?\n• Windows says "protected your PC", what do I do?' },
        { name: 'When it doesn\'t know', value: 'It says so and points you to <#{help}>, where a person will help. Bugs go in <#{bugs}>, wrongly flagged files in <#{falsepositives}>.' },
        { name: 'Please', value: 'One question per message. Never post passwords, API keys or addon links: the bot removes them, and you should create a new key if you did. No piracy talk (see the <#{rules}>). You can also type `/ask` in any channel.' },
      ],
      footer: 'The bot can be wrong. A moderator can always correct it.',
    },
  ],

  // ------------------------------------------------------------------------------------------------ Lumen guides
  lumenhow: [
    {
      title: 'How Lumen works', color: RED, thumbnail: '{assetBase}/emoji-lumen.png', url: '{lumenSite}',
      description: ':lumen: Lumen is a **home screen and a player**. It doesn\'t come with movies or shows. It shows what the **addons** you add can find and plays what your own accounts allow. Three layers:',
      fields: [
        { name: '1 · Addons: where everything comes from', value: 'Lumen works with Stremio-compatible addons. **Catalog** addons fill your rows (popular titles, Top 10, anime lists). **Stream** addons supply the sources you pick from when you press Play. **Subtitle** addons add subtitles; OpenSubtitles is installed from the start.' },
        { name: '2 · Real-Debrid (optional): torrent sources', value: 'Real-Debrid is a separate paid service with its own account. Link yours and the torrent sources your addons find can be played, and the files already in your Real-Debrid account appear in Library. It is optional; Lumen works without it.' },
        { name: '3 · Lumen: the part you see', value: 'A home built from your catalogs (a featured title, Continue watching, a Top 10), sources ranked by quality with your usual pick marked, and a player with subtitles, audio languages, skip intro and next episode. On Windows it also fixes Dolby and DTS sound as it plays.' },
        { name: 'Your data', value: 'Everything works without an account. A free Lumen account only keeps your profiles, lists, Continue watching and addons the same on every PC, phone and TV. Your addons are encrypted with your PIN before they leave your device, and your Real-Debrid key stays on the device.' },
        { name: 'Your responsibility', value: 'Lumen doesn\'t host or provide any content. You choose the addons and you are responsible for how you use them. The <#{rules}> apply here: no piracy talk.' },
      ],
    },
    {
      embeds: [
        { title: 'What you see', color: RED, url: '{lumenSite}', image: '{assetBase}/lumen/home.jpg', description: '**Home** is built from your catalogs. **Movies**, **Series** and **Anime** are full pages with their own Top 10 and filters; **Search** looks across every addon at once; **Library** keeps your lists and progress.' },
        { color: RED, url: '{lumenSite}', image: '{assetBase}/lumen/detail.jpg' },
        { color: RED, url: '{lumenSite}', image: '{assetBase}/lumen/anime-calendar.jpg' },
        { color: RED, url: '{lumenSite}', image: '{assetBase}/lumen/themes.jpg' },
      ],
    },
    {
      title: 'Where to go next', color: RED,
      description: 'Set it up step by step in <#{lumenguide}>. Chat about it in <#{lumenchat}>. Problems go in <#{help}> with the **Lumen** tag. Phones and TVs are in <#{lumendevices}>.',
    },
  ],

  lumenguide: [
    {
      title: 'Set up addons and Real-Debrid', color: RED, thumbnail: '{assetBase}/emoji-lumen.png', url: '{lumenSite}/windows/', image: '{assetBase}/lumen/realdebrid.jpg',
      description: 'Six steps, in the order to do them. Real-Debrid and some addons are separate services with their own prices, and **you are responsible for how you use them**: Lumen doesn\'t host or provide any content.',
      fields: [
        { name: '1 · Install and open Lumen', value: '[Download it]({lumenDownload}) and open it. A first home screen from the default catalogs appears right away, with no setup.' },
        { name: '2 · Get addons', value: 'Open **Addons**. Under **Get addons** are popular ones. Those with an **Add** button install in one click (catalog addons such as The Movie Database or Anime Kitsu). Those that need an account or a key show **Set up** or **Set up on its page** and open their setup page right inside Lumen. You can also paste the link of an addon you already use.' },
        { name: '3 · Link Real-Debrid (optional)', value: 'Open **Settings → Real-Debrid & Usenet** and press **Link Real-Debrid**, then follow the prompts. The status changes from "Not linked". You need your own Real-Debrid account.' },
        { name: '4 · Torrent addons need your key too', value: 'Torrent addons such as Comet keep your Real-Debrid API key in their own setup. If you get a new key, update them from **Settings → Real-Debrid & Usenet** (the "Real-Debrid key in your addons" box) instead of setting each one up again.' },
        { name: '5 · Usenet (optional)', value: 'In the same Settings page, **Usenet** lets you add a TorBox API key and press **Link**. It plays the Usenet (NZB) results your addons find, and torrents when Real-Debrid isn\'t linked.' },
        { name: '6 · Press play', value: 'Open a title. The sources your addons found are listed by quality with your usual pick marked, and Lumen warns you when a sound format needs fixing. Pick one and watch.' },
      ],
    },
    {
      title: 'If something isn\'t working', color: GOLD,
      fields: [
        { name: 'No sources for a title', value: 'Most often no stream addon is installed yet, or the addon still needs its key (check **Addons** and the Real-Debrid page in Settings).' },
        { name: 'Empty rows on Home', value: 'The catalog addons couldn\'t load. Check your internet connection, then look at **Addons**.' },
        { name: 'A warning on a source about sound', value: 'On Windows Lumen fixes Dolby and DTS sound as it plays. On Android and Samsung TV, sources with only DTS sound are marked when the device can\'t play them.' },
        { name: 'Still stuck?', value: 'Make a post in <#{help}> with the **Lumen** tag: your Lumen version, your device, which addons you use (their names only) and the exact message you see.' },
      ],
    },
    {
      title: 'Keep your keys private', color: RED, thumbnail: '{assetBase}/emoji-alert.png',
      description: 'Never post your **Real-Debrid API key**, your **TorBox key** or the **link of an addon you set up**: many addon links contain your key inside them, so anyone who sees the link can use your account. If you posted one by mistake, delete the message and create a new key.\n\nThis server doesn\'t share addon lists or help find content, and a request for it will be removed (see the <#{rules}>).',
    },
  ],

  // ------------------------------------------------------------------------------------------------ Lumen Studios
  studioschat: [
    gallery('{studiosSite}', VIOLET, {
      title: 'Lumen Studios: edit videos the easy way', thumbnail: '{assetBase}/emoji-studios.png',
      description: ':studios: A video editor that looks good and is easy to understand. Cut clips on a multi-track timeline, add titles, stickers, effects, transitions and filters, caption your video automatically, then export a sharp MP4. Free, no account, no watermark.\n\n**[Download for Windows]({studiosDownload})** · [Website]({studiosSite}) · videos you make go in <#{studiosshowcase}>',
    }, ['{studiosMedia}/editor.jpg', '{studiosMedia}/captions.jpg']),
    {
      title: 'Everything you need to make a video', color: VIOLET,
      description: 'All the tools sit on the left. Pick one, click what you like, and it lands on your timeline.',
      fields: [
        { name: 'Multi-track timeline', value: 'Tracks for video, overlays, text and audio. Split, trim, ripple delete, change speed, reverse, add keyframes and snap clips together.' },
        { name: 'Text & stickers', value: 'Twelve text styles with in and out animations, and about a hundred emoji stickers you can drag, scale and animate.' },
        { name: 'Effects & body effects', value: 'Over 75 effects like glow, VHS, glitch, light leaks, kaleidoscope and snow, plus 22 body effects (aura, neon outline, clones, blur background, cutout) that follow the person in your clip.' },
        { name: '94 transitions', value: 'Cube, flip, page turn, heart, film burn, glitch and more, in ten groups. Hover one to see it play, click to apply it to a cut, or to every cut at once.' },
        { name: 'Filters & adjust', value: 'Nineteen colour looks, plus exposure, contrast, highlights, shadows, vibrance, fade and sharpen sliders you can reset in one click.' },
        { name: 'Captions', value: 'Generate captions from speech, offline on your own PC, or import `.srt` and `.vtt` files. Style them once and export them back out.' },
        { name: 'Templates', value: '32 ready-made titles, looks and transition packs: intros, lower thirds, neon nights, old movie, comic book and more.' },
        { name: 'Sharp MP4 export', value: 'Frame-accurate H.264 and AAC, including HDR footage tone-mapped for normal screens. Projects autosave and recover after a crash.' },
      ],
    },
    {
      title: 'Start editing in a minute', color: VIOLET,
      description: '**1. Download & install.** Run the installer. Windows may say "Windows protected your PC" because Lumen Studios isn\'t code-signed yet: choose *More info*, then *Run anyway*.\n**2. Import & arrange.** Drop in your videos, photos and music, add them to the timeline, split and trim until it flows.\n**3. Polish & export.** Add text, effects, transitions and captions, then press *Export* for an MP4 you can share anywhere.\n\nProblems? Make a post in <#{help}> with the Lumen Studios tag.',
    },
  ],

  studiosshowcase: [
    {
      embeds: [
        {
          title: 'The official gallery, and your videos', color: VIOLET, thumbnail: '{assetBase}/emoji-studios.png',
          description: 'This channel is for **videos you made with Lumen Studios**: post a clip or a link, say what you used (effects, transitions, templates), and tell us what you would do differently next time. Below is what the editor looks like.',
        },
      ],
    },
    { embeds: [{ title: 'Captions', color: VIOLET, description: 'Captions on their own track, styled in one go.', image: '{studiosMedia}/captions.jpg' }] },
    { embeds: [{ title: 'Effects', color: VIOLET, description: 'Effects, with live previews.', image: '{studiosMedia}/effects.jpg' }] },
    { embeds: [{ title: 'Transitions', color: VIOLET, description: 'Transitions that play when you hover.', image: '{studiosMedia}/transitions.jpg' }] },
    { embeds: [{ title: 'Filters', color: VIOLET, description: 'Colour filters.', image: '{studiosMedia}/filters.jpg' }] },
    { embeds: [{ title: 'Animations', color: VIOLET, description: 'Animations you can see on the clip.', image: '{studiosMedia}/animations.jpg' }] },
    { embeds: [{ title: 'Templates', color: VIOLET, description: 'Ready-made templates.', image: '{studiosMedia}/templates.jpg' }] },
    { embeds: [{ title: 'Text styles', color: VIOLET, description: 'Twelve text styles with in and out animations.', image: '{studiosMedia}/text.jpg' }] },
  ],

  studioseffects: [
    gallery('{studiosSite}', VIOLET, {
      title: 'Effects, transitions, filters and templates', thumbnail: '{assetBase}/emoji-studios.png',
      description: 'Everything in the left-hand tool rail, in numbers. Share how you got a look in this channel.\n\n**75+ effects:** glow, VHS, glitch, light leaks, kaleidoscope, snow and more.\n**22 body effects** that follow the person in your clip: aura, neon outline, clones, blur background, cutout.\n**94 transitions** in ten groups: cube, flip, page turn, heart, film burn, glitch. Hover to preview, click to apply to one cut or every cut.\n**19 colour looks** plus exposure, contrast, highlights, shadows, vibrance, fade and sharpen sliders.\n**12 text styles** with in and out animations, and about a hundred emoji stickers.\n**32 templates:** intros, lower thirds, neon nights, old movie, comic book and more.',
    }, ['{studiosMedia}/effects.jpg', '{studiosMedia}/transitions.jpg', '{studiosMedia}/filters.jpg', '{studiosMedia}/templates.jpg']),
    {
      title: 'Share a look', color: VIOLET,
      description: 'A good post here says which effect or transition you used, in which order, and shows the result.',
      fields: [{ name: 'Template', value: '```\nLook: what it is (e.g. old movie, neon night)\nMade with: effect + filter + transition names\nTip: one thing that made it work\n```' }],
    },
  ],

  // ------------------------------------------------------------------------------------------------ Guardian
  guardianchat: [
    gallery('{guardianSite}', CYAN, {
      title: 'Guardian: your PC, under guard', thumbnail: '{assetBase}/emoji-guardian.png',
      description: ':guardian: A free antivirus and PC-care app for Windows 10 and 11 with its own scan engine, real-time protection and a switch for every shield. It never opens your documents, photos or ID scans, never uploads your files and never removes anything you can\'t undo.\n\n**[Download]({guardianDownload})** · [Website]({guardianSite}) · [How it works](<#{howitworks}>)',
    }, ['{guardianMedia}/overview.jpg', '{guardianMedia}/protection.jpg', '{guardianMedia}/protection-2.jpg', '{guardianMedia}/tools.jpg']),
    {
      title: 'Scan your way', color: CYAN,
      description: 'The deep scan is the most thorough, but you never have to run it.',
      fields: [
        { name: ':scan: Quick scan', value: 'Startup entries, running programs and the places malware lands first: Downloads, temp folders, AppData. A few minutes.' },
        { name: ':usb: Folder scan', value: 'Pick one folder, like Downloads or a USB stick, and check just that, subfolders included.' },
        { name: ':protected: Deep scan', value: 'Everything above plus kernel drivers, security settings and every hiding place, with signature rules on top.' },
        { name: 'Extras', value: 'Windows Defender quick, full and folder scans when Defender is on, and a VirusTotal hash check (fingerprints only, files are never uploaded).' },
      ],
    },
    {
      title: 'Real-time protection', color: CYAN,
      description: 'Every shield has its own switch: new files, startup entries, USB drives and, as administrator, **programs as they start** through Windows\' own kernel process events. Optional auto-quarantine and an optional pause for high-risk programs. Guardian is not a kernel driver: it can pause a program but can\'t stop it before its first instruction.',
      fields: [{ name: 'Need help?', value: 'Make a post in <#{help}> with the Guardian tag. Wrongly flagged file? <#{falsepositives}>.' }],
    },
  ],

  threat: [
    {
      title: 'Threat talk', color: CYAN, thumbnail: '{assetBase}/emoji-alert.png',
      description: 'How malware hides, how detection works and how to stay safe. Defensive only, and **never post live malware or links to it**: names, hashes (SHA-256) and descriptions are fine.',
      fields: [
        { name: 'Where Guardian looks', value: 'Startup entries (Run keys, startup folders, scheduled tasks, services, Winlogon), WMI subscriptions, IFEO debuggers, AppInit DLLs, running programs, kernel drivers, temp/AppData/ProgramData/Public/Recycle Bin folders, and security settings attackers switch off.' },
        { name: 'How it weighs evidence', value: 'A valid signature, an odd location, a disguised name, a known-bad hash or a YARA rule hit each add evidence. A rule hit is weighed against the rule\'s confidence and the file\'s signature, never a verdict by itself.' },
        { name: 'Good topics', value: '• A technique you saw and how to spot it\n• Why a certain location is a favourite hiding place\n• How to read a Guardian finding\n• Safe habits: updates, backups, checking downloads' },
      ],
    },
  ],

  yara: [
    {
      title: 'YARA and detection rules', color: CYAN, thumbnail: '{assetBase}/emoji-scan.png',
      description: 'Share and review YARA rules and detection ideas. Guardian downloads a curated rule set and also loads your own.',
      fields: [
        { name: 'Using your own rules in Guardian', value: 'Put `.yar` files in `%LOCALAPPDATA%\\Guardian\\intel\\custom` and apply them in Settings under *Threat intelligence*.' },
        { name: 'A skeleton to start from', value: '```\nrule My_Example : example\n{\n  meta:\n    description = "what it detects"\n    author = "you"\n  strings:\n    $a = "a distinctive string" ascii wide\n  condition:\n    $a\n}\n```' },
        { name: 'Posting a rule', value: 'Say what it detects, what you tested it against, and what could make it false-positive. Never post the malware itself.' },
      ],
    },
  ],

  howitworks: [
    {
      title: 'How Guardian works', color: CYAN, thumbnail: '{assetBase}/emoji-guardian.png', image: '{guardianMedia}/protection-2.jpg',
      description: 'Four steps, every time something new shows up on your PC.',
      fields: [
        { name: '1. Catch', value: 'Watchers see new files in Downloads, the desktop, temp and startup folders, new startup entries, USB drives and, as administrator, every program the moment it starts (through Windows\' own kernel process events).' },
        { name: '2. Inspect', value: 'The engine checks the digital signature, where the file lives and how it behaves, then 9,000+ known-bad fingerprints and 5,000+ YARA rules. All of it runs on your PC. A rule match counts as evidence weighed against the rule\'s confidence and the file\'s signature, never as a verdict by itself.' },
        { name: '3. Decide', value: 'Anything medium-risk or worse becomes an alert with the reasons in plain words. Optionally Guardian quarantines serious threats or pauses a high-risk program until you decide.' },
        { name: '4. Undo', value: 'Quarantine moves a file aside and never deletes it. Restore it any time. Every alert stays in your history.' },
      ],
      footer: 'Guardian never opens your documents, photos or ID scans, and never uploads your files.',
    },
  ],

  // ------------------------------------------------------------------------------------------------ development
  changelog: [
    {
      title: 'Guardian release history', color: GREEN, thumbnail: '{assetBase}/emoji-guardian.png',
      description: 'The short version of how Guardian got here. Every release has full notes on [GitHub]({guardianRepo}/releases).',
      fields: [
        { name: '1.2', value: 'Guardian\'s own deep scan engine, known-bad hash lists and YARA rules.' },
        { name: '1.3 – 1.5', value: 'Registry cleaner (including HKEY_CLASSES_ROOT), Driver Easy-style driver tools, and the System tools hub.' },
        { name: '1.6 – 1.7', value: 'BIOS update assistant that never flashes firmware, debloat, shortcut fixer, tracks eraser, manufacturer driver checks, performance tweaks.' },
        { name: '1.8', value: 'Real-time protection with its own switch for every shield, tray icon and background mode.' },
        { name: '1.9', value: 'Programs are checked as they start, through Windows\' kernel event feed, with an optional pause. A new look for the Protection page.' },
        { name: '1.10', value: 'Quick and folder scans with Guardian\'s own engine, and Guardian works without Windows Defender.' },
      ],
    },
  ],

  contributing: [
    {
      title: 'Want to help?', color: VIOLET, thumbnail: '{assetBase}/emoji-play.png',
      description: 'These are one-person projects that are happy to have company.',
      fields: [
        { name: 'Test', value: 'Try new versions of any app and tell us what breaks. Ask a moderator for the **Beta Tester** role.' },
        { name: 'Report', value: 'Bugs in <#{bugs}>, ideas in <#{features}>, Guardian false positives in <#{falsepositives}>. Pick the app in the tags.' },
        { name: 'Write rules', value: 'Share YARA rules in <#{yara}>. Guardian loads your own rules from `%LOCALAPPDATA%\\Guardian\\intel\\custom`.' },
        { name: 'Look at the code', value: 'Guardian is open on [GitHub]({guardianRepo}). Open an issue before a big change.' },
      ],
    },
  ],
};

// The first post pinned in each forum, so the forum is never empty and people know how to post.
export const seeds = {
  help: {
    title: 'How to ask for help', tags: ['Solved'], skipTagsIfMissing: true,
    embeds: [{
      color: RED, thumbnail: '{assetBase}/emoji-lumen.png',
      description: 'Make a new post and pick the app in the tags. The more you tell us, the faster the answer.',
      fields: [
        { name: 'Always include', value: '• Which app and which version\n• Your Windows version, or your phone or TV model\n• What you did, what you expected, what happened\n• The exact error message, as text or a screenshot' },
        { name: ':guardian: Guardian', value: 'Say whether you ran it as administrator and attach the end of `%LOCALAPPDATA%\\Guardian\\errors.log` if there is an error.' },
        { name: ':lumen: Lumen', value: 'Say which device (Windows, Android, Samsung TV) and which addon or source if it is about playback. Never post your Real-Debrid key.' },
        { name: ':studios: Lumen Studios', value: 'Say what was on the timeline, the file type you imported and what you exported.' },
        { name: 'When it is fixed', value: 'Add the **Solved** tag so others can find the answer.' },
      ],
    }],
  },
  bugs: {
    title: 'How to report a bug', tags: [],
    embeds: [{
      color: RED, thumbnail: '{assetBase}/emoji-alert.png',
      description: 'Found a bug? Pick the app in the tags and write it up like this:',
      fields: [{ name: 'Template', value: '```\nApp and version:\nWhat I expected:\nWhat happened:\nHow to make it happen again:\nScreenshot or error text:\n```' }],
    }],
  },
  features: {
    title: 'How to suggest a feature', tags: [],
    embeds: [{
      color: GOLD, thumbnail: '{assetBase}/emoji-play.png',
      description: 'One idea per post. Pick the app in the tags, say what problem it solves, and vote on other ideas with a reaction.',
      fields: [
        { name: 'Template', value: '```\nApp:\nThe problem:\nMy idea:\nHow I work around it today:\n```' },
        { name: 'About the roadmap', value: 'See <#{roadmap}>. Ideas are welcome, but nothing is promised.' },
      ],
    }],
  },
  falsepositives: {
    title: 'How to report a false positive', tags: [],
    embeds: [{
      color: CYAN, thumbnail: '{assetBase}/emoji-guardian.png',
      description: 'Guardian flagged something that is fine? That is a bug worth fixing. Post these details:',
      fields: [
        { name: 'Include', value: '• The file name and the program it belongs to\n• The publisher, and where you got it\n• The **SHA-256** shown in the finding\'s details\n• The *Why it was flagged* text (a screenshot is fine)' },
        { name: ':alert: Never', value: 'Do **not** upload files that may hold personal data, and don\'t post the file itself if it could be harmful. The hash is enough.' },
        { name: 'What happens next', value: 'The post is tagged **Fixed**, **Not a false positive** or **Needs more info**, and real ones become a GitHub issue.' },
      ],
    }],
  },
};
