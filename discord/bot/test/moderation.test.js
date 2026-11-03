import test from 'node:test';
import assert from 'node:assert/strict';
import { Moderator } from '../src/moderation.js';
import { Store } from '../src/store.js';

const cfg = (extra = {}) => ({ dryRun: false, staffRoles: ['Lumen Dev', 'Moderator'], modLogChannel: 'mod-log', ignoreChannels: ['mod-chat', 'mod-log'], allowedInvites: ['76PgqgZgjV'], mentionLimit: 5, ...extra });

function world({ staff = false, dry = false } = {}) {
  const sent = { notices: [], dms: [], logs: [], deleted: 0, timeouts: [] };
  const logChannel = { id: 'LOG', name: '🚨｜mod-log', isTextBased: () => true, send: async (m) => { sent.logs.push(m); } };
  const modRole = { id: 'R1', name: 'Moderator', mentionable: true };
  const guild = {
    name: 'Lumen', channels: { cache: { find: (fn) => [logChannel].find(fn), get: (id) => (id === 'LOG' ? logChannel : undefined) } }, roles: { cache: { find: (fn) => [modRole].find(fn) } },
    members: { fetch: async () => member },
  };
  const member = {
    joinedTimestamp: Date.now() - 86400_000 * 40, permissions: { has: () => false },
    roles: { cache: { some: (fn) => (staff ? [modRole].some(fn) : false) } },
    timeout: async (ms) => { sent.timeouts.push(ms); },
  };
  const message = (content, extra = {}) => ({
    guild, member, content, author: { id: 'U1', username: 'sam', tag: 'sam', bot: false, createdTimestamp: Date.now() - 86400_000 * 800, send: async (t) => { sent.dms.push(t); } },
    channel: { id: 'C1', name: '💬｜general', send: async (m) => { const n = { delete: async () => {} }; sent.notices.push(m); return n; } },
    attachments: new Map(), mentions: { users: { size: 0 }, roles: { size: 0 }, everyone: false },
    delete: async () => { sent.deleted++; }, ...extra,
  });
  const store = new Store(':memory:');
  const moderator = new Moderator({ store, cfg: cfg({ dryRun: dry }), client: {} });
  return { sent, message, moderator, store };
}

test('staff are never moderated', async () => {
  const w = world({ staff: true });
  assert.equal(await w.moderator.handle(w.message('free nitro discord.gift/x')), null);
  assert.equal(w.sent.deleted, 0);
});

test('an executable attachment is removed with an explanation, without a strike', async () => {
  const w = world();
  const m = w.message('here you go', { attachments: new Map([['1', { name: 'Guardian-Setup.exe' }]]) });
  const r = await w.moderator.handle(m);
  assert.equal(r.deleted, true);
  assert.equal(w.sent.deleted, 1);
  assert.equal(r.strikes, 0, 'a file notice is not a strike');
  assert.match(w.sent.notices[0].content, /removed your message/);
  assert.match(w.sent.dms[0], /hash|GitHub/);
  assert.equal(w.sent.logs.length, 1);
});

test('a scam is removed, the member is timed out for a day and the staff are asked', async () => {
  const w = world();
  const r = await w.moderator.handle(w.message('FREE NITRO claim your prize http://x.example'));
  assert.equal(r.deleted, true);
  assert.deepEqual(w.sent.timeouts, [24 * 60 * 60_000]);
  assert.equal(r.askStaff, true);
  assert.match(w.sent.logs[0].content, /<@&R1>/, 'the moderator role is pinged');
});

test('a leaked key is removed, never copied into the log, and the member is told to rotate it', async () => {
  const w = world();
  const token = ['MTIzNDU2Nzg5MDEyMzQ1Njc4OQ', 'AbCdEf', 'abcdefghijklmnopqrstuvwxyzAB'].join('.');
  const r = await w.moderator.handle(w.message(`my bot token ${token}`));
  assert.equal(r.deleted, true);
  assert.ok(!JSON.stringify(w.sent.logs).includes('MTIzNDU2'), 'the log must not hold the secret');
  assert.ok(!JSON.stringify(w.sent.notices).includes('MTIzNDU2'));
  assert.match(w.sent.dms[0], /new one/);
});

test('strikes escalate: warning, then 10 minutes, then an hour', async () => {
  const w = world();
  for (const expected of [[0, 1], [10 * 60_000, 2], [60 * 60_000, 3]]) {
    const r = await w.moderator.handle(w.message('get the cracked version here'));
    assert.equal(r.strikes, expected[1]);
    assert.equal(r.timeoutMs, expected[0]);
  }
  assert.deepEqual(w.sent.timeouts, [10 * 60_000, 60 * 60_000]);
});

test('dry run changes nothing but still logs what it would do', async () => {
  const w = world({ dry: true });
  const r = await w.moderator.handle(w.message('FREE NITRO discord.gift/abc'));
  assert.equal(r.deleted, 'would');
  assert.equal(w.sent.deleted, 0);
  assert.deepEqual(w.sent.timeouts, []);
  assert.equal(w.sent.notices.length, 0);
  assert.equal(w.sent.dms.length, 0);
  assert.match(w.sent.logs[0].embeds[0].data.title, /dry run/);
});

test('bots, webhooks and the staff channels are skipped', async () => {
  const w = world();
  const bot = w.message('free nitro'); bot.author.bot = true;
  assert.equal(await w.moderator.handle(bot), null);
  const hook = w.message('free nitro', { webhookId: 'W1' });
  assert.equal(await w.moderator.handle(hook), null);
  const staffChat = w.message('free nitro'); staffChat.channel.name = '🛡️｜mod-chat';
  assert.equal(await w.moderator.handle(staffChat), null);
});

test('the strike store counts, lists and clears', () => {
  const s = new Store(':memory:');
  s.addStrike({ userId: 'a', rule: 'piracy', reason: 'x', at: Date.now() - 40 * 86400_000 });
  assert.equal(s.countRecent('a'), 0, 'strikes older than 30 days do not count');
  assert.equal(s.addStrike({ userId: 'a', rule: 'scam', reason: 'y' }), 1);
  assert.equal(s.list('a').length, 2);
  assert.equal(s.clear('a'), 2);
  assert.equal(s.countRecent('a'), 0);
});
