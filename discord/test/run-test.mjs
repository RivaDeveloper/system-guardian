// Runs setup.mjs against the in-memory mock server and checks the result:
//   1. a fresh build from server.json
//   2. a second run, which must change nothing
//   3. Community unavailable: forums must fall back to text channels
//   4. an UPGRADE: a server built from the old Guardian-only design (test/guardian-v1.json) is migrated to the current
//      design with --refresh; renamed roles/categories/channels must be renamed in place (not duplicated) and the old
//      first messages replaced.
//   node test/run-test.mjs
import { spawn, spawnSync } from 'node:child_process';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';
import assert from 'node:assert/strict';

const here = dirname(fileURLToPath(import.meta.url));
const setup = join(here, '..', 'setup.mjs');
const cfg = JSON.parse(readFileSync(join(here, '..', 'server.json'), 'utf8'));
const old = JSON.parse(readFileSync(join(here, 'guardian-v1.json'), 'utf8'));

async function withMock(port, extra, fn) {
  const mock = spawn(process.execPath, [join(here, 'mock-discord.mjs'), String(port), ...extra], { stdio: 'ignore' });
  await new Promise((r) => setTimeout(r, 700));
  try { await fn(); } finally { mock.kill(); }
}
const run = (port, flags = [], config) => spawnSync(process.execPath, [setup, ...flags], {
  env: { ...process.env, DISCORD_API: `http://127.0.0.1:${port}/api/v10`, DISCORD_TOKEN: 'test-token', GUILD_ID: 'G1', ...(config ? { SERVER_CONFIG: config } : {}) }, encoding: 'utf8',
});
const state = async (port) => (await fetch(`http://127.0.0.1:${port}/__state`)).json();
const count = (c) => ({
  roles: c.roles.length,
  categories: c.categories.length,
  channels: c.categories.reduce((n, k) => n + k.channels.length, 0),
  forums: c.categories.flatMap((k) => k.channels).filter((x) => x.type === 'forum').length,
});
const want = count(cfg);
const totals = (s) => JSON.stringify({ r: s.roles.length, c: s.channels.length, m: Object.values(s.messages).flat().length, a: s.automod.length, h: Object.values(s.webhooks).flat().length });

// ---- runs 1 and 2 -----------------------------------------------------------------------------------------------
await withMock(8801, [], async () => {
  const r1 = run(8801);
  assert.equal(r1.status, 0, r1.stderr || r1.stdout);
  let s = await state(8801);
  assert.equal(s.guild.name, cfg.name, 'server renamed');
  assert.equal(s.roles.length - 1, want.roles, 'roles created');
  assert.equal(s.channels.filter((c) => c.type === 4).length, want.categories, 'categories created');
  assert.equal(s.channels.filter((c) => c.type !== 4).length, want.channels, 'channels created');
  assert.equal(s.channels.filter((c) => c.type === 15).length, want.forums, 'forum channels created');
  assert.ok(s.channels.filter((c) => c.type === 15).every((c) => c.available_tags.length > 0), 'forums have tags');
  assert.ok(s.channels.some((c) => c.type === 5), 'an announcement channel exists');
  const posted = s.channels.filter((c) => (s.messages[c.id] || []).length).length;
  const wantPosted = cfg.categories.flatMap((k) => k.channels).filter((c) => c.post).length;
  assert.equal(posted, wantPosted, 'first messages posted');
  const text = JSON.stringify(s.messages);
  assert.ok(!/\{\w+\}/.test(text.replace(/<#\d+>/g, '')), 'no unfilled {placeholders} in messages');
  assert.ok(/<#\d+>/.test(text) && !/<#\{/.test(text), 'channel mentions were turned into ids');
  assert.equal(s.automod.length, 3, 'automod rules');
  assert.equal(Object.values(s.webhooks).flat().length, 0, 'no bot-owned webhook unless --webhook is given');
  // pictures, custom emoji and forum starter posts
  const welcomeCh = s.channels.find((c) => c.name.includes('welcome'));
  const welcomeEmbeds = s.messages[welcomeCh.id].flatMap((m) => m.embeds);
  assert.ok(welcomeEmbeds.some((e) => e.image?.url?.endsWith('/banner.png')), 'welcome has the banner picture');
  assert.ok(welcomeEmbeds.filter((e) => e.thumbnail?.url).length >= 3, 'project cards have icons');
  assert.ok(/<:lumen:\d+>/.test(text), 'custom emoji turned into <:name:id>');
  assert.ok(!/(?<![<\w]):[a-z]+:(?!\d)/.test(text.replace(/https?:\/\/\S+/g, '')), 'no leftover :emoji: tokens');
  const studiosShow = s.channels.find((c) => c.name.includes('studios-showcase'));
  assert.ok(s.messages[studiosShow.id].length >= 5, 'studios-showcase is filled with the gallery');
  const emptyTexts = s.channels.filter((c) => c.type === 0 && !(s.messages[c.id] || []).length).map((c) => c.name);
  console.log('  text channels still empty:', JSON.stringify(emptyTexts));
  assert.equal(s.threads.length, want.forums, 'a starter post in every forum');
  assert.ok(s.threads.every((t) => t.flags === 2), 'forum starter posts are pinned');
  const staff = s.channels.find((c) => c.name.includes('STAFF'));
  assert.ok(staff.permission_overwrites.some((o) => o.id === 'G1' && o.deny !== '0'), 'staff category hidden from everyone');
  console.log('run 1 ok:', JSON.stringify({ roles: s.roles.length - 1, channels: s.channels.length, posted }));

  const before = totals(s);
  const r2 = run(8801);
  assert.equal(r2.status, 0, r2.stderr || r2.stdout);
  s = await state(8801);
  assert.equal(totals(s), before, 'a second run must not create anything new');
  console.log('run 2 ok: nothing duplicated');
});

// ---- Community unavailable ------------------------------------------------------------------------------------
await withMock(8802, ['--no-community'], async () => {
  const r = run(8802);
  assert.equal(r.status, 0, r.stderr || r.stdout);
  const s = await state(8802);
  assert.equal(s.channels.filter((c) => c.type === 15).length, 0, 'no forums without Community');
  assert.equal(s.channels.filter((c) => c.type !== 4).length, want.channels, 'forums fell back to text channels');
  console.log('run 3 ok: falls back to text channels when Community is unavailable');
});

// ---- upgrade from the Guardian-only server -----------------------------------------------------------------------
await withMock(8803, [], async () => {
  const r1 = run(8803, [], join(here, 'guardian-v1.json'));
  assert.equal(r1.status, 0, r1.stderr || r1.stdout);
  let s = await state(8803);
  const oldCount = count(old);
  assert.equal(s.channels.filter((c) => c.type !== 4).length, oldCount.channels, 'old design built');
  const oldRoleIds = s.roles.filter((r) => r.name === 'Guardian Dev').map((r) => r.id);
  const oldSecurity = s.channels.find((c) => c.type === 4 && c.name.includes('SECURITY LAB'));
  const oldHowItWorks = s.channels.find((c) => c.name.includes('how-it-works'));
  assert.ok(oldRoleIds.length === 1 && oldSecurity && oldHowItWorks, 'old names present before the upgrade');

  const r2 = run(8803, ['--refresh']);
  assert.equal(r2.status, 0, r2.stderr || r2.stdout);
  s = await state(8803);
  assert.equal(s.guild.name, 'Lumen', 'server renamed to Lumen');
  const lumenDev = s.roles.find((r) => r.name === 'Lumen Dev');
  assert.ok(lumenDev && oldRoleIds.includes(lumenDev.id), 'Guardian Dev was renamed, not duplicated');
  assert.ok(!s.roles.some((r) => r.name === 'Guardian Dev'), 'old role name gone');
  assert.equal(s.channels.find((c) => c.id === oldSecurity.id).name, '🛡️ GUARDIAN', 'SECURITY LAB category renamed in place');
  assert.equal(s.channels.find((c) => c.id === oldHowItWorks.id).name, '⚙️｜how-guardian-works', 'how-it-works renamed in place');
  assert.equal(s.channels.filter((c) => c.type === 4).length, want.categories, 'categories match the new design');
  assert.equal(s.channels.filter((c) => c.type !== 4).length, want.channels, 'channels match the new design (no duplicates)');
  const welcome = s.channels.find((c) => c.name.includes('welcome'));
  const welcomeText = JSON.stringify(s.messages[welcome.id]);
  assert.ok(welcomeText.includes('The streaming app') && !welcomeText.includes('Welcome to Guardian'), 'old welcome message replaced');
  assert.equal(s.messages[welcome.id].length, 2, 'exactly the two new welcome messages (nothing left of the old one)');
  const helpForum = s.channels.find((c) => c.type === 15 && c.name.includes('help'));
  assert.ok(helpForum.available_tags.some((t) => t.name === 'Lumen Studios'), 'forum tags updated for all projects');
  console.log('run 4 ok: the Guardian server was upgraded to Lumen in place');

  const before = totals(s);
  const r3 = run(8803);
  assert.equal(r3.status, 0, r3.stderr || r3.stdout);
  assert.equal(totals(await state(8803)), before, 'running again after the upgrade changes nothing');
  console.log('run 5 ok: stable after the upgrade');
});
console.log('\nAll checks passed.');
