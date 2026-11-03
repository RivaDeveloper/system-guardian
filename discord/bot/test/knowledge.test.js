import test from 'node:test';
import assert from 'node:assert/strict';
import { Knowledge, isConfident, isSure } from '../src/knowledge.js';
import { Answerer } from '../src/answer.js';
import { askClaude, AiBudget } from '../src/ai.js';

const kb = new Knowledge();
const best = (q) => kb.search(q, 1)[0];

test('the knowledge base finds the right answer to real questions', () => {
  const cases = [
    ['do I need windows defender for guardian', 'Do I need Windows Defender?'],
    ['how do I link real-debrid', 'Link Real-Debrid'],
    ['windows says protected your pc when installing', 'protected your PC'],
    ['how do I add addons in lumen', 'How do I add addons in Lumen?'],
    ['does guardian upload my files', 'upload my files'],
    ['which file should i download for my arm laptop', 'Which file should I download?'],
    ['can I use guardian without admin rights', 'administrator rights'],
    ['how do i uninstall guardian', 'uninstall'],
    ['my message got deleted by the bot', 'Why was my message deleted?'],
    ['is guardian free', 'free'],
  ];
  for (const [q, expect] of cases) {
    const r = best(q);
    assert.ok(r, q);
    assert.ok(r.title.toLowerCase().includes(expect.toLowerCase()), `${q} -> ${r.title}`);
    assert.ok(isConfident(r), `${q} should be confident (score ${r.score.toFixed(1)})`);
  }
});

test('it stays quiet about things it does not know', () => {
  for (const q of ['what is the weather today', 'lol that is funny', 'good morning everyone', 'who won the match yesterday', 'what time is it']) {
    const r = best(q);
    assert.ok(!isConfident(r), `${q} -> ${r?.title} (${r?.score?.toFixed(1)})`);
  }
});

test('unprompted replies need a clear question and a sure match', () => {
  const a = new Answerer({ cfg: { autoAnswer: true, answerChannels: ['general'] }, kb, env: {} });
  const m = (content, extra = {}) => ({ content, author: { id: 'u1', bot: false }, guild: {}, channel: { id: 'c1', name: '💬｜general' }, reference: null, mentions: { users: { size: 0 } }, ...extra });
  assert.ok(a.considerAutoAnswer(m('Do I need Windows Defender to run Guardian?'), 1_000_000));
  assert.equal(a.considerAutoAnswer(m('Do I need Windows Defender to run Guardian?'), 1_000_100), null, 'channel cooldown');
  const b = new Answerer({ cfg: { autoAnswer: true, answerChannels: ['general'] }, kb, env: {} });
  assert.equal(b.considerAutoAnswer(m('I need windows defender lol')), null, 'not a question');
  assert.equal(b.considerAutoAnswer(m('Do I need Windows Defender to run Guardian?', { reference: { messageId: '1' } })), null, 'a reply inside a conversation');
  assert.equal(b.considerAutoAnswer(m('Do I need Windows Defender to run Guardian?', { channel: { id: 'c2', name: 'mod-chat' } })), null, 'wrong channel');
  assert.equal(b.considerAutoAnswer(m('what should we have for dinner tonight guys?')), null, 'unrelated');
});

test('answer(): knowledge base only when there is no API key, and a clear no-answer otherwise', async () => {
  const a = new Answerer({ cfg: {}, kb, env: {} });
  const ok = await a.answer('how do I link real-debrid');
  assert.ok(ok.confident && !ok.usedAi && /Link Real-Debrid/i.test(ok.text));
  const none = await a.answer('what is the capital of france');
  assert.equal(none.text, null);
});

test('AI mode: grounded prompt, rate limit, and a graceful fallback', async () => {
  const calls = [];
  const fakeFetch = async (url, init) => { calls.push({ url, body: JSON.parse(init.body), headers: init.headers }); return { ok: true, json: async () => ({ content: [{ type: 'text', text: 'Open Settings → Real-Debrid & Usenet and press Link.' }] }) }; };
  const env = { ANTHROPIC_API_KEY: 'test-key', AI_PER_USER_PER_HOUR: '2' };
  const a = new Answerer({ cfg: {}, kb, env, fetchImpl: fakeFetch });
  const r = await a.answer('how do I link real-debrid', { userId: 'u1' });
  assert.ok(r.usedAi && r.text.includes('Settings'));
  assert.equal(calls[0].headers['x-api-key'], 'test-key');
  assert.ok(calls[0].body.system.includes('ONLY the reference passages'));
  assert.ok(calls[0].body.messages[0].content.includes('Link Real-Debrid'), 'the best passages are sent along');
  await a.answer('how do I link real-debrid', { userId: 'u1' });
  const third = await a.answer('how do I link real-debrid', { userId: 'u1' });
  assert.equal(third.usedAi, false, 'the third call in an hour falls back to the knowledge base');
  assert.ok(third.text, 'and still answers');
  const broken = new Answerer({ cfg: {}, kb, env, fetchImpl: async () => { throw new Error('network down'); } });
  const f = await broken.answer('how do I link real-debrid', { userId: 'u9' });
  assert.ok(!f.usedAi && f.text, 'an AI failure falls back to the knowledge base');
  assert.equal(await askClaude('x', [], {}), null, 'no key, no call');
  assert.ok(new AiBudget(1).take('a') && true);
});

test('forum replies carry a checklist and only a confident answer', () => {
  const a = new Answerer({ cfg: {}, kb, env: {} });
  const r = a.forumReply('help', 'Windows protected my PC message', 'When I run the installer Windows says protected your PC, what do I do?');
  assert.ok(r.best && /More info/i.test(r.best.text));
  assert.match(r.checklist, /version/);
  const none = a.forumReply('bug-reports', 'Crash', 'It crashes sometimes.');
  assert.equal(none.best, null);
});
