// Runs setup.mjs against the in-memory mock server and checks the result: first run, a second run (must change nothing),
// and a run where Community can't be enabled (forums must fall back to text channels).
//   node test/run-test.mjs
import { spawn, spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';
import assert from 'node:assert/strict';

const here = dirname(fileURLToPath(import.meta.url));
const setup = join(here, '..', 'setup.mjs');
const cfg = JSON.parse((await import('node:fs')).readFileSync(join(here, '..', 'server.json'), 'utf8'));

async function withMock(port, extra, fn) {
  const mock = spawn(process.execPath, [join(here, 'mock-discord.mjs'), String(port), ...extra], { stdio: 'ignore' });
  await new Promise((r) => setTimeout(r, 700));
  try { await fn(); } finally { mock.kill(); }
}
const run = (port) => spawnSync(process.execPath, [setup, '--skip-emoji'], {
  env: { ...process.env, DISCORD_API: `http://127.0.0.1:${port}/api/v10`, DISCORD_TOKEN: 'test-token', GUILD_ID: 'G1' }, encoding: 'utf8',
});
const state = async (port) => (await fetch(`http://127.0.0.1:${port}/__state`)).json();
const want = {
  roles: cfg.roles.length,
  categories: cfg.categories.length,
  channels: cfg.categories.reduce((n, c) => n + c.channels.length, 0),
  forums: cfg.categories.flatMap((c) => c.channels).filter((c) => c.type === 'forum').length,
};

// ---- run 1 and run 2 -------------------------------------------------------------------------------------------
await withMock(8801, [], async () => {
  const r1 = run(8801);
  assert.equal(r1.status, 0, r1.stderr || r1.stdout);
  let s = await state(8801);
  assert.equal(s.roles.length - 1, want.roles, 'roles created');
  assert.equal(s.channels.filter((c) => c.type === 4).length, want.categories, 'categories created');
  assert.equal(s.channels.filter((c) => c.type !== 4).length, want.channels, 'channels created');
  assert.equal(s.channels.filter((c) => c.type === 15).length, want.forums, 'forum channels created');
  assert.ok(s.channels.filter((c) => c.type === 15).every((c) => c.available_tags.length > 0), 'forums have tags');
  assert.ok(s.channels.some((c) => c.type === 5), 'an announcement channel exists');
  const posted = Object.values(s.messages).filter((m) => m.length).length;
  assert.ok(posted >= 8, 'first messages posted (' + posted + ')');
  assert.ok(!JSON.stringify(s.messages).includes('{rules}'), 'no unfilled placeholders');
  assert.ok(/<#\d+>/.test(JSON.stringify(s.messages)) && !/<#\{/.test(JSON.stringify(s.messages)), 'channel mentions were turned into ids');
  assert.equal(s.automod.length, 3, 'automod rules');
  const hooks = Object.values(s.webhooks).flat();
  assert.equal(hooks.length, 1, 'release webhook');
  const staff = s.channels.find((c) => c.name.includes('mod-chat'));
  assert.ok(staff && s.channels.find((c) => c.name.includes('STAFF')).permission_overwrites.some((o) => o.id === 'G1' && o.deny !== '0'), 'staff category hidden from everyone');
  console.log('run 1 ok:', JSON.stringify({ roles: s.roles.length - 1, channels: s.channels.length, posted, automod: s.automod.length }));

  const before = JSON.stringify({ r: s.roles.length, c: s.channels.length, m: Object.values(s.messages).flat().length, a: s.automod.length, h: Object.values(s.webhooks).flat().length });
  const r2 = run(8801);
  assert.equal(r2.status, 0, r2.stderr || r2.stdout);
  s = await state(8801);
  const after = JSON.stringify({ r: s.roles.length, c: s.channels.length, m: Object.values(s.messages).flat().length, a: s.automod.length, h: Object.values(s.webhooks).flat().length });
  assert.equal(after, before, 'a second run must not create anything new');
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
console.log('\nAll checks passed.');
