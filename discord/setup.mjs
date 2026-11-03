#!/usr/bin/env node
// Builds the whole Discord server from server.json (the Lumen community: Lumen, Lumen Studios and Guardian): roles, categories, channels, permissions, forum channels
// with tags, the first messages, AutoMod rules, emoji, the release webhook and a permanent invite.
//
//   DISCORD_TOKEN=<bot token> GUILD_ID=<server id> node setup.mjs [--dry-run] [--refresh] [--webhook] [--github] [--skip-emoji]
//   --refresh  also replaces the bot's earlier first messages (use it after changing the texts in server.json)
//   --webhook  also creates a bot-owned release webhook (it is deleted if the bot is removed: prefer making your own)
//
// It is safe to run again: roles, channels and emoji are matched by name (or by an earlier name listed under "was", so a
// rename is applied instead of duplicating) and reused, and a message is only posted into a channel that has no messages yet. See README.md for how to get a bot token and the server id.

import { readFileSync, existsSync } from 'node:fs';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';

const here = dirname(fileURLToPath(import.meta.url));
const args = new Set(process.argv.slice(2));
const DRY = args.has('--dry-run');
const API = process.env.DISCORD_API || 'https://discord.com/api/v10';
const TOKEN = process.env.DISCORD_TOKEN || '';
const GUILD = process.env.GUILD_ID || '';
const cfg = JSON.parse(readFileSync(process.env.SERVER_CONFIG || join(here, 'server.json'), 'utf8'));
const content = cfg.messages ? { messages: cfg.messages, seeds: {} } : await import('./content.mjs');
cfg.messages = content.messages;

if (!DRY && (!TOKEN || !GUILD)) {
  console.error('Set DISCORD_TOKEN and GUILD_ID first (see README.md), or use --dry-run to only print the plan.');
  process.exit(1);
}

// ---- permission bits ------------------------------------------------------------------------------------
const P = {
  CREATE_INSTANT_INVITE: 0, KICK_MEMBERS: 1, BAN_MEMBERS: 2, ADMINISTRATOR: 3, VIEW_AUDIT_LOG: 7, ADD_REACTIONS: 6,
  VIEW_CHANNEL: 10, SEND_MESSAGES: 11, MANAGE_MESSAGES: 13, EMBED_LINKS: 14, ATTACH_FILES: 15, READ_MESSAGE_HISTORY: 16,
  MENTION_EVERYONE: 17, CONNECT: 20, SPEAK: 21, MANAGE_NICKNAMES: 27, MANAGE_THREADS: 34, CREATE_PUBLIC_THREADS: 35,
  SEND_MESSAGES_IN_THREADS: 38, MODERATE_MEMBERS: 40,
};
const bits = (names) => String(names.reduce((acc, n) => { if (!(n in P)) throw new Error('Unknown permission ' + n); return acc | (1n << BigInt(P[n])); }, 0n));
const colorInt = (hex) => parseInt(hex.replace('#', ''), 16);

// ---- tiny REST client with rate-limit handling ---------------------------------------------------------------
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
let calls = 0;
async function api(method, path, body) {
  if (DRY) { console.log(`  [dry-run] ${method} ${path}`); return {}; }
  for (let attempt = 0; attempt < 6; attempt++) {
    const res = await fetch(API + path, {
      method,
      headers: { Authorization: 'Bot ' + TOKEN, 'Content-Type': 'application/json', 'User-Agent': 'GuardianServerSetup (https://github.com/RivaDeveloper/system-guardian, 1.0)' },
      body: body === undefined ? undefined : JSON.stringify(body),
    });
    calls++;
    if (res.status === 429) {
      const wait = (await res.json().catch(() => ({}))).retry_after ?? 2;
      console.log(`  rate limited, waiting ${wait}s`);
      await sleep(Math.ceil(wait * 1000) + 100);
      continue;
    }
    const text = await res.text();
    const data = text ? JSON.parse(text) : {};
    if (!res.ok) { const e = new Error(`${method} ${path} -> ${res.status} ${data.message || text}`); e.status = res.status; e.code = data.code; throw e; }
    await sleep(350);   // stay well under the global rate limit
    return data;
  }
  throw new Error(`${method} ${path} kept hitting the rate limit`);
}
async function tryApi(label, method, path, body) {
  try { return await api(method, path, body); }
  catch (e) { console.log(`  ! ${label} skipped: ${e.message}`); return null; }
}

const step = (t) => console.log(`\n== ${t}`);
const dataUri = (file) => `data:image/${file.endsWith('.gif') ? 'gif' : 'png'};base64,${readFileSync(join(here, 'assets', file)).toString('base64')}`;

// ---- helpers ------------------------------------------------------------------------------------------------
const plain = (name) => name.toLowerCase().replace(/^[^\p{L}\p{N}]+/u, '').replace(/^[｜|]+/u, '').replace(/[^\p{L}\p{N}]+/gu, '-').replace(/^-|-$/g, '');
const matches = (existingName, item) => [item.name, ...(item.was || [])].some((n) => plain(n) === plain(existingName));
const ids = {};          // channel key -> id
const roleIds = {};      // role key -> id

const RUN = Date.now().toString(36);   // picture cache-buster, see fillEmbed
const emojiIds = {};     // emoji name -> id, filled in once the emoji exist
function fill(text) {
  return String(text)
    .replace(/<#\{(\w+)\}>/g, (_, k) => (ids[k] ? `<#${ids[k]}>` : '#' + k))
    .replace(/\{(\w+)\}/g, (_, k) => cfg.links[k] ?? `{${k}}`)
    .replace(/(?<![<\w]):([a-z]+):(?!\d)/g, (m, n) => (emojiIds[n] ? `<:${n}:${emojiIds[n]}>` : ''));
}
function fillEmbed(embed) {
  const e = JSON.parse(JSON.stringify(embed));
  for (const k of ['title', 'description', 'url']) if (e[k]) e[k] = fill(e[k]);
  if (e.fields) e.fields = e.fields.map((f) => ({ ...f, value: fill(f.value), name: fill(f.name) }));
  if (e.footer && typeof e.footer === 'string') e.footer = { text: fill(e.footer) };
  // Discord keeps its own copy of every picture URL it has seen, so a picture that changed on a website would still show the
  // old one. A new ?v= on each run makes Discord fetch the current file.
  for (const k of ['image', 'thumbnail']) if (typeof e[k] === 'string') { const u = fill(e[k]); e[k] = { url: u + (u.includes('?') ? '&' : '?') + 'v=' + RUN }; }
  return e;
}
const toBody = (entry) => ({ embeds: (entry.embeds || [entry]).map(fillEmbed), allowed_mentions: { parse: [] } });

// ---- 0. what we are starting from ------------------------------------------------------------------------------
console.log(DRY ? 'DRY RUN: nothing will be sent to Discord.' : `Setting up server ${GUILD}`);
const guild = DRY ? { id: 'GUILD', features: [] } : await api('GET', `/guilds/${GUILD}`);
const everyone = guild.id;
const existingRoles = DRY ? [] : await api('GET', `/guilds/${GUILD}/roles`);
let existingChannels = DRY ? [] : await api('GET', `/guilds/${GUILD}/channels`);

// ---- 1. server settings and icon ------------------------------------------------------------------------------
step('Server settings');
await api('PATCH', `/guilds/${GUILD}`, { name: cfg.name, ...cfg.settings, icon: dataUri('icon.png') });

// ---- 2. roles --------------------------------------------------------------------------------------------------
step('Roles');
for (const r of cfg.roles) {
  const found = existingRoles.find((x) => x.id !== everyone && !x.managed && [r.name, ...(r.was || [])].includes(x.name));
  const body = { name: r.name, color: colorInt(r.color), hoist: r.hoist, mentionable: r.mentionable, permissions: bits(r.permissions) };
  if (found) { roleIds[r.key] = found.id; await api('PATCH', `/guilds/${GUILD}/roles/${found.id}`, body); console.log('  updated ' + r.name); }
  else { const made = await api('POST', `/guilds/${GUILD}/roles`, body); roleIds[r.key] = made.id; console.log('  created ' + r.name); }
}
// a stable order: dev on top, then mod, contributor, researcher, beta
if (!DRY) await tryApi('role order', 'PATCH', `/guilds/${GUILD}/roles`, cfg.roles.map((r, i) => ({ id: roleIds[r.key], position: cfg.roles.length - i })));

// ---- 3. categories and channels (text + voice first; forums and announcements need Community) --------------
const TYPE = { text: 0, voice: 2, category: 4, announcement: 5, forum: 15 };
const READ_ONLY_DENY = ['SEND_MESSAGES', 'CREATE_PUBLIC_THREADS', 'SEND_MESSAGES_IN_THREADS'];

function overwrites(cat, ch) {
  const o = [];
  const staff = ['dev', 'mod'].map((k) => roleIds[k]).filter(Boolean);
  const hidden = cat.private || ch.access;
  if (hidden) {
    o.push({ id: everyone, type: 0, allow: '0', deny: bits(['VIEW_CHANNEL']) });
    for (const k of cat.private ?? ch.access) if (roleIds[k]) o.push({ id: roleIds[k], type: 0, allow: bits(['VIEW_CHANNEL', 'SEND_MESSAGES', 'READ_MESSAGE_HISTORY']), deny: '0' });
  } else if (ch.readonly) {
    o.push({ id: everyone, type: 0, allow: bits(['VIEW_CHANNEL', 'READ_MESSAGE_HISTORY', 'ADD_REACTIONS']), deny: bits(READ_ONLY_DENY) });
    for (const id of staff) o.push({ id, type: 0, allow: bits(['SEND_MESSAGES', 'EMBED_LINKS', 'ATTACH_FILES', 'MANAGE_MESSAGES']), deny: '0' });
  }
  return o;
}

step('Categories and channels');
let position = 0;
const pendingLater = [];   // forums and announcement channels, created once Community is on
for (const cat of cfg.categories) {
  let category = existingChannels.find((c) => c.type === TYPE.category && matches(c.name, cat));
  const catOverwrites = cat.private ? [
    { id: everyone, type: 0, allow: '0', deny: bits(['VIEW_CHANNEL']) },
    ...cat.private.map((k) => ({ id: roleIds[k], type: 0, allow: bits(['VIEW_CHANNEL', 'SEND_MESSAGES', 'READ_MESSAGE_HISTORY']), deny: '0' })).filter((x) => x.id),
  ] : [];
  if (!category) {
    category = await api('POST', `/guilds/${GUILD}/channels`, { name: cat.name, type: TYPE.category, position: position++, permission_overwrites: catOverwrites });
    console.log('  + category ' + cat.name);
    existingChannels.push(category);
  } else {
    if (category.name !== cat.name) await api('PATCH', `/channels/${category.id}`, { name: cat.name, ...(catOverwrites.length ? { permission_overwrites: catOverwrites } : {}) });
    console.log('  = category ' + cat.name);
  }
  cat._id = category.id;

  for (const ch of cat.channels) {
    if (ch.type === 'forum') { pendingLater.push({ cat, category, ch }); continue; }
    let found = existingChannels.find((c) => (c.type === TYPE.text || c.type === TYPE.voice || c.type === TYPE.announcement) && matches(c.name, ch));
    const body = {
      name: ch.name, type: TYPE[ch.type], parent_id: category.id, topic: ch.topic, rate_limit_per_user: ch.slowmode || 0,
      permission_overwrites: cat.private ? [] : overwrites(cat, ch),
    };
    if (ch.type === 'voice') { delete body.topic; delete body.rate_limit_per_user; }
    if (!found) { found = await api('POST', `/guilds/${GUILD}/channels`, body); console.log('  + ' + ch.name); existingChannels.push(found); }
    else { await api('PATCH', `/channels/${found.id}`, { ...body, type: undefined }); console.log('  = ' + ch.name); }
    ids[ch.key] = found.id;
  }
}

// ---- 4. Community, the rules and updates channels ----------------------------------------------------------------
step('Community features');
let community = !DRY && (guild.features || []).includes('COMMUNITY');
if (!community) {
  const r = await tryApi('Community (turn it on in Server Settings > Enable Community if this fails)', 'PATCH', `/guilds/${GUILD}`, {
    features: [...new Set([...(guild.features || []), 'COMMUNITY'])],
    rules_channel_id: ids.rules, public_updates_channel_id: ids.modlog,
    preferred_locale: 'en-US', verification_level: Math.max(1, cfg.settings.verification_level), explicit_content_filter: 2,
  });
  community = !!r || DRY;
}
console.log(community ? '  Community is on.' : '  Community is off: forum channels become text channels and announcements stay normal channels.');
if (ids.general) await tryApi('system channel', 'PATCH', `/guilds/${GUILD}`, { system_channel_id: ids.general, system_channel_flags: 4 | 8 });

// ---- 5. forums (or text fallbacks) and announcement channels ----------------------------------------------
step('Forums and announcements');
for (const { category, ch } of pendingLater) {
  const asForum = community;
  let found = existingChannels.find((c) => (c.type === TYPE.forum || c.type === TYPE.text) && matches(c.name, ch));
  const body = {
    name: ch.name, type: asForum ? TYPE.forum : TYPE.text, parent_id: category.id, topic: ch.topic,
    ...(asForum ? { available_tags: (ch.tags || []).map((t) => ({ name: t })), default_reaction_emoji: null, rate_limit_per_user: 0 } : {}),
  };
  if (!found) { found = await api('POST', `/guilds/${GUILD}/channels`, body); console.log(`  + ${ch.name} (${asForum ? 'forum' : 'text'})`); }
  else {
    const upd = { name: ch.name, topic: ch.topic, parent_id: category.id };
    if (found.type === TYPE.forum) upd.available_tags = (ch.tags || []).map((t) => { const old = (found.available_tags || []).find((o) => o.name === t); return old ? { id: old.id, name: t } : { name: t }; });
    await api('PATCH', `/channels/${found.id}`, upd);
    console.log(`  = ${ch.name}`);
  }
  ids[ch.key] = found.id;
}
// ---- channel order inside each category, as written in server.json (new channels land where they belong) ----------
if (!DRY) {
  const wantedOrder = [];
  for (const cat of cfg.categories) for (const ch of cat.channels) if (ids[ch.key]) wantedOrder.push({ id: ids[ch.key], position: wantedOrder.length });
  await tryApi('channel order', 'PATCH', `/guilds/${GUILD}/channels`, wantedOrder);
}
if (community) {
  for (const cat of cfg.categories) for (const ch of cat.channels) {
    if (!ch.announcement || !ids[ch.key]) continue;
    await tryApi('announcement channel ' + ch.name, 'PATCH', `/channels/${ids[ch.key]}`, { type: TYPE.announcement });
  }
}

if (!DRY) await tryApi('category order', 'PATCH', `/guilds/${GUILD}/channels`, cfg.categories.filter((c) => c._id).map((c, i) => ({ id: c._id, position: i })));

// ---- emoji (before the messages, which use them) ----------------------------------------------------------------
if (!args.has('--skip-emoji')) {
  step('Emoji');
  const haveEmoji = DRY ? [] : await api('GET', `/guilds/${GUILD}/emojis`);
  for (const name of cfg.emoji) {
    if (haveEmoji.some((e) => e.name === name)) { console.log('  = :' + name + ':'); continue; }
    const made = await tryApi('emoji ' + name, 'POST', `/guilds/${GUILD}/emojis`, { name, image: dataUri(`emoji-${name}.png`) });
    if (made) console.log('  + :' + name + ':');
  }
}
for (const e of DRY ? [] : await api('GET', `/guilds/${GUILD}/emojis`)) emojiIds[e.name] = e.id;

// ---- 6. first messages (only into empty channels) -------------------------------------------------------------
step('Messages');
const me = DRY || !args.has('--refresh') ? { id: '' } : await api('GET', '/users/@me');
for (const cat of cfg.categories) for (const ch of cat.channels) {
  const channelId = ids[ch.key];
  const embeds = ch.post && cfg.messages[ch.post];
  if (!channelId || !embeds) continue;
  let existing = DRY ? [] : await api('GET', `/channels/${channelId}/messages?limit=50`);
  if (args.has('--refresh')) {
    for (const m of existing.filter((x) => x.author?.id === me.id)) await tryApi('delete old message', 'DELETE', `/channels/${channelId}/messages/${m.id}`);
    existing = existing.filter((x) => x.author?.id !== me.id);
  }
  if (existing.length) { console.log(`  = #${ch.key} already has messages`); continue; }
  for (const e of embeds) {
    const msg = await api('POST', `/channels/${channelId}/messages`, toBody(e));
    if (msg.id && ch.pin) await tryApi('pin', 'PUT', `/channels/${channelId}/pins/${msg.id}`);
    console.log(`  + posted into #${ch.key}`);
  }
}


// ---- 6b. a pinned first post in every forum -----------------------------------------------------------------------
step('Forum starter posts');
let threads = [];
if (!DRY) threads = (await tryApi('listing threads', 'GET', `/guilds/${GUILD}/threads/active`))?.threads || [];
for (const cat of cfg.categories) for (const ch of cat.channels) {
  if (ch.type !== 'forum' || !ch.seed || !content.seeds?.[ch.seed] || !ids[ch.key]) continue;
  const seed = content.seeds[ch.seed];
  const have = threads.find((t) => t.parent_id === ids[ch.key] && t.name === seed.title);
  if (have) { console.log(`  = ${seed.title}`); continue; }
  const forum = DRY ? { available_tags: [] } : existingChannels.find((c) => c.id === ids[ch.key]) || {};
  const live = DRY ? forum : await api('GET', `/channels/${ids[ch.key]}`);
  const tagIds = (seed.tags || []).map((t) => (live.available_tags || []).find((x) => x.name === t)?.id).filter(Boolean);
  const made = await tryApi('forum post ' + seed.title, 'POST', `/channels/${ids[ch.key]}/threads`, {
    name: seed.title, applied_tags: tagIds, message: toBody(seed),
  });
  if (made?.id) { console.log(`  + ${seed.title}`); await tryApi('pin forum post', 'PATCH', `/channels/${made.id}`, { flags: 2 }); }
}

// ---- 7. AutoMod ------------------------------------------------------------------------------------------------
step('AutoMod');
const alertTo = ids.modlog ? [{ type: 2, metadata: { channel_id: ids.modlog } }] : [];
const rules = [
  { name: 'Guardian: block mention spam', event_type: 1, trigger_type: 5, trigger_metadata: { mention_total_limit: cfg.automod.mentionLimit }, actions: [{ type: 1 }, ...alertTo], enabled: true },
  { name: 'Guardian: block scam phrases', event_type: 1, trigger_type: 1, trigger_metadata: { keyword_filter: cfg.automod.keywords.map((k) => `*${k}*`) }, actions: [{ type: 1 }, ...alertTo], enabled: true },
  { name: 'Guardian: block spam', event_type: 1, trigger_type: 3, actions: [{ type: 1 }, ...alertTo], enabled: true },
];
const haveRules = DRY ? [] : (await tryApi('listing AutoMod rules', 'GET', `/guilds/${GUILD}/auto-moderation/rules`)) || [];
for (const r of rules) {
  if (haveRules.some((x) => x.name === r.name)) { console.log('  = ' + r.name); continue; }
  const made = await tryApi(r.name, 'POST', `/guilds/${GUILD}/auto-moderation/rules`, r);
  if (made) console.log('  + ' + r.name);
}

// ---- 9. release webhook and invite ---------------------------------------------------------------------------
step('Release webhook');
let hookUrl = '';
if (!args.has('--webhook')) console.log('  Skipped. Create your own webhook (Server Settings > Integrations > Webhooks > New Webhook, channel #releases) and add it to each GitHub repo; see README.md. Use --webhook to make a bot-owned one anyway.');
if (args.has('--webhook') && ids.releases) {
  const hooks = DRY ? [] : (await tryApi('listing webhooks', 'GET', `/channels/${ids.releases}/webhooks`)) || [];
  let hook = hooks.find((h) => h.name === 'Guardian Releases');
  if (!hook) hook = await tryApi('webhook', 'POST', `/channels/${ids.releases}/webhooks`, { name: 'Guardian Releases', avatar: dataUri('icon.png') });
  if (hook?.token) hookUrl = `https://discord.com/api/webhooks/${hook.id}/${hook.token}`;
}
if (hookUrl) {
  console.log('  Note: this webhook is owned by the bot and Discord deletes it when the bot is removed. Create your own in Server Settings > Integrations > Webhooks before you remove the bot, and use that URL for GitHub.');
  const github = (cfg.links.guardianRepo || cfg.links.github).replace('https://github.com/', '');
  if (args.has('--github')) {
    const gh = spawnSync('gh', ['api', '-X', 'POST', `repos/${github}/hooks`, '--input', '-'], {
      input: JSON.stringify({ name: 'web', active: true, events: ['release'], config: { url: hookUrl + '/github', content_type: 'json' } }), encoding: 'utf8',
    });
    console.log(gh.status === 0 ? `  GitHub release webhook added to ${github}.` : `  ! Could not add the GitHub webhook (${(gh.stderr || gh.error || '').toString().trim()}). Add it by hand, see README.md.`);
  } else {
    console.log('  Webhook created. Add it to GitHub (repo > Settings > Webhooks) with this payload URL:');
    console.log('  ' + hookUrl + '/github');
    console.log('  Content type application/json, events: Releases only. Or run again with --github to let the gh CLI do it.');
  }
}

step('Invite');
if (ids.welcome) {
  const inv = await tryApi('invite', 'POST', `/channels/${ids.welcome}/invites`, { max_age: 0, max_uses: 0, unique: false });
  if (inv?.code) console.log('  Permanent invite: https://discord.gg/' + inv.code);
}

// ---- 10. banner and splash (need server boosts; harmless if they fail) ---------------------------------------
step('Banner and invite splash');
// The animated versions need more boosts (icon: level 1, banner: level 3); the still ones are the fallback.
const animatedIcon = existsSync(join(here, 'assets', 'icon-animated.gif')) && await tryApi('animated icon (needs Boost level 1)', 'PATCH', `/guilds/${GUILD}`, { icon: dataUri('icon-animated.gif') });
if (animatedIcon) console.log('  animated icon set');
const animatedBanner = existsSync(join(here, 'assets', 'banner-animated.gif')) && await tryApi('animated banner (needs Boost level 3)', 'PATCH', `/guilds/${GUILD}`, { banner: dataUri('banner-animated.gif') });
if (animatedBanner) console.log('  animated banner set');
else await tryApi('banner (needs Boost level 2)', 'PATCH', `/guilds/${GUILD}`, { banner: dataUri('banner.png') });
await tryApi('invite splash (needs Boost level 1)', 'PATCH', `/guilds/${GUILD}`, { splash: dataUri('splash.png') });

console.log(`\nDone${DRY ? ' (dry run)' : ` in ${calls} requests`}. Next: read README.md "After the script" for the few things only the Discord app can do.`);
