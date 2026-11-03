import test from 'node:test';
import assert from 'node:assert/strict';
import { evaluate, FloodTracker } from '../src/rules.js';
import { escalate } from '../src/escalation.js';

const msg = (content, extra = {}) => ({ content, attachmentNames: [], mentionCount: 0, mentionsEveryone: false, accountAgeDays: 400, minutesSinceJoin: 5000, ...extra });
const rules = (m, o) => evaluate(m, { allowedInvites: ['76PgqgZgjV'], ...o }).map((f) => f.rule);

test('normal conversation is left alone', () => {
  for (const t of [
    'Guardian keeps flagging my game launcher, is that normal?', 'how do I link real debrid in lumen?', 'I love the new Protection page!',
    'does lumen studios export 4k?', 'is Lumen a pirate site? my friend says it is', 'here is the sha256 of the file: 3a7bd3e2360a3d29eea436fcfb7e44c735d117c42d1c1835420b6b9942dd4f1b',
    'check https://github.com/RivaDeveloper/system-guardian/releases/latest', 'my cracked screen is annoying lol',
  ]) assert.deepEqual(rules(msg(t)), [], t);
});

test('keys and tokens are caught, and never echoed in the reason', () => {
  const token = ['MTIzNDU2Nzg5MDEyMzQ1Njc4OQ', 'AbCdEf', 'abcdefghijklmnopqrstuvwxyzAB'].join('.');
  const rd = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789ABCDEFGHIJKLMNOP';
  assert.equal(rd.length, 52);
  const addon = 'https://torrentio.example.com/realdebrid=' + 'A1b2C3d4E5f6G7h8I9j0K1l2M3n4O5p6Q7r8S9t0U1v2' + '/manifest.json';
  for (const t of [`my token is ${token}`, `key: ${rd}`, `install this ${addon}`, 'ghp_' + 'a'.repeat(36), '-----BEGIN RSA PRIVATE KEY-----']) {
    const f = evaluate(msg(t), {});
    assert.ok(f.some((x) => x.rule === 'secret'), t.slice(0, 30));
    assert.ok(f.every((x) => !x.reason.includes('MTU2') && !x.reason.includes('ABCDEFGH')), 'the secret must not appear in the reason');
  }
  assert.ok(evaluate(msg('https://getlumen.pages.dev/windows/'), {}).length === 0, 'an ordinary link is fine');
  assert.ok(evaluate(msg('addon: https://v3-cinemeta.strem.io/manifest.json'), {}).length === 0, 'a plain public manifest has no key in it');
});

test('scams are high severity', () => {
  for (const t of ['FREE NITRO for everyone, claim your prize here', 'discord.gift/abc123', 'join the airdrop now', 'steam gift for you http://discord-nitro.xyz/x'])
    assert.ok(evaluate(msg(t)).some((f) => f.rule === 'scam' && f.severity === 'high'), t);
});

test('piracy: links and cracks are caught, questions are not', () => {
  for (const t of ['magnet:?xt=urn:btih:abcdef1234', 'try 1337x for it', 'get the cracked version here', 'keygen inside', 'where can I watch this for free']) assert.ok(rules(msg(t)).includes('piracy'), t);
  for (const t of ['does lumen help with piracy? asking because of the rules', 'how does the free tier of real-debrid work']) assert.ok(!rules(msg(t)).includes('piracy'), t);
});

test('invites: only the server\'s own is allowed', () => {
  assert.ok(rules(msg('join discord.gg/abc123 now')).includes('invite'));
  assert.ok(!rules(msg('join https://discord.gg/76PgqgZgjV')).includes('invite'));
});

test('attachments that can hold programs are blocked, logs and pictures are not', () => {
  assert.ok(rules(msg('look', { attachmentNames: ['setup.exe'] })).includes('attachment'));
  assert.ok(rules(msg('look', { attachmentNames: ['Photos.ZIP'] })).includes('attachment'));
  assert.deepEqual(rules(msg('look', { attachmentNames: ['errors.log', 'screenshot.png', 'notes.txt', 'rule.yar'] })), []);
});

test('mention spam and fresh accounts with links', () => {
  assert.ok(rules(msg('hey', { mentionCount: 9 })).includes('mentions'));
  assert.ok(rules(msg('@everyone read this', { mentionsEveryone: true })).includes('mentions'));
  assert.ok(rules(msg('see https://example.com/page', { minutesSinceJoin: 2 })).includes('new-link'));
  assert.ok(!rules(msg('see https://example.com/page', { minutesSinceJoin: 3000, accountAgeDays: 900 })).includes('new-link'));
});

test('flood and repeats', () => {
  const f = new FloodTracker();
  let hit = null;
  for (let i = 0; i < 9; i++) hit = f.record('u1', `msg ${i}`, 1000 + i * 100) || hit;
  assert.equal(hit?.rule, 'flood');
  const g = new FloodTracker();
  assert.equal(g.record('u2', 'buy my thing now', 0), null);
  assert.equal(g.record('u2', 'buy my thing now', 5000), null);
  assert.equal(g.record('u2', 'buy my thing now', 9000)?.rule, 'repeat');
  assert.equal(g.record('u3', 'ok', 0), null); assert.equal(g.record('u3', 'ok', 1000), null); assert.equal(g.record('u3', 'ok', 2000), null, 'very short messages are not repeats');
});

test('escalation: gentle first, never a ban', () => {
  assert.equal(escalate(1, 'medium').timeoutMs, 0);
  assert.equal(escalate(2, 'medium').timeoutMs, 10 * 60_000);
  assert.equal(escalate(3, 'medium').timeoutMs, 60 * 60_000);
  assert.equal(escalate(4, 'medium').askStaff, true);
  assert.equal(escalate(9, 'medium').timeoutMs, 7 * 24 * 60 * 60_000);
  assert.equal(escalate(1, 'high').timeoutMs, 24 * 60 * 60_000);
  assert.equal(escalate(1, 'high').askStaff, true);
});
