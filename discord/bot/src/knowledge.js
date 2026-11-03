// The bot's knowledge: the FAQ, the guides and the roadmap already written for the server (content.mjs, the same words people can
// read in the channels) plus a few extra questions in kb-extra.json. Search is plain BM25, so it works offline and costs nothing.
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';
import { messages } from '../../content.mjs';

const here = dirname(fileURLToPath(import.meta.url));

const STOP = new Set(('a an the and or but if then so of to in on at by for from with without about into over is are was were be been being do does did doing have has had having can could should would will '
  + 'it its this that these those i me my we our you your they them their he she him her what which who whom how why when where there here not no yes just also very really please hi hello hey thanks thank '
  + 'any some more most other such only own same than too s t get got').split(' '));

export function tokenize(text) {
  return (String(text).toLowerCase().match(/[a-z0-9]+/g) || [])
    .filter((w) => !STOP.has(w))
    .map((w) => w.replace(/(?:ing|ed|es|s)$/, '') || w);   // light stemming: "addons"/"addon", "linking"/"link"
}

function clean(text) {
  return String(text).replace(/```[\s\S]*?```/g, ' ').replace(/[*_`>|]/g, '').replace(/\[([^\]]+)\]\([^)]*\)/g, '$1');
}

/** Flattens content.mjs and the extra file into small searchable pieces. */
export function buildDocs() {
  const docs = [];
  // the ask-the-bot post lists example questions; indexing it would make the bot answer a question with another question
  const SKIP = new Set(['askbot']);
  for (const [source, list] of Object.entries(messages)) {
    if (SKIP.has(source)) continue;
    for (const entry of list) {
      for (const e of entry.embeds || [entry]) {
        const topic = e.title || '';
        if (e.description && e.description.length > 40) docs.push({ title: topic, text: e.description, source, topic });
        for (const f of e.fields || []) docs.push({ title: f.name, text: f.value, source, topic });
      }
    }
  }
  try {
    for (const item of JSON.parse(readFileSync(join(here, '..', 'kb-extra.json'), 'utf8'))) {
      docs.push({ title: item.q[0], text: item.a, source: item.source || 'extra', topic: item.topic || 'Extra', extraQuestions: item.q.slice(1) });
    }
  } catch { /* no extra file */ }
  return docs;
}

export class Knowledge {
  constructor(docs = buildDocs()) {
    this.docs = docs.map((d) => {
      const titleTokens = tokenize(clean(d.title + ' ' + (d.extraQuestions || []).join(' ')));
      const bodyTokens = tokenize(clean(d.text));
      const tokens = [...titleTokens, ...titleTokens, ...bodyTokens];    // the title counts double
      const tf = new Map(); for (const t of tokens) tf.set(t, (tf.get(t) || 0) + 1);
      // alternate phrasings of a written question: an exact match on the meaningful words beats the word statistics
      const phrasings = [d.title, ...(d.extraQuestions || [])].map((p) => [...new Set(tokenize(clean(p)))].sort().join(' ')).filter(Boolean);
      return { ...d, tf, len: tokens.length, phrasings: d.extraQuestions ? phrasings : [] };
    });
    this.avg = this.docs.reduce((a, d) => a + d.len, 0) / Math.max(1, this.docs.length);
    this.df = new Map();
    for (const d of this.docs) for (const t of d.tf.keys()) this.df.set(t, (this.df.get(t) || 0) + 1);
  }

  /** Returns the best pieces, most relevant first, each with a score. */
  search(question, limit = 3) {
    const q = [...new Set(tokenize(question))];
    if (!q.length) return [];
    const qKey = [...q].sort().join(" ");
    const N = this.docs.length, k1 = 1.4, b = 0.75;
    const scored = this.docs.map((d) => {
      let score = 0, hits = 0;
      for (const t of q) {
        const f = d.tf.get(t); if (!f) continue;
        hits++;
        const idf = Math.log(1 + (N - this.df.get(t) + 0.5) / (this.df.get(t) + 0.5));
        score += idf * ((f * (k1 + 1)) / (f + k1 * (1 - b + (b * d.len) / this.avg)));
      }
      score *= 0.6 + 0.4 * (hits / q.length);
      if (d.phrasings.includes(qKey)) score = Math.max(score, 20);
      return { doc: d, score, hits, of: q.length };
    }).filter((x) => x.score > 0).sort((a, b) => b.score - a.score);
    return scored.slice(0, limit).map((x) => ({ ...x.doc, score: x.score, hits: x.hits, of: x.of }));
  }
}

// How sure the bot must be. A short question can only match a few words, so a result also counts when every word matched.
/** Good enough to present as "the answer" when someone asks (/ask, a mention, a new help post). */
export const isConfident = (r) => !!r && (r.score >= 7.5 || (r.hits >= 2 && r.hits === r.of && r.score >= 4.5));
/** Good enough to speak up unprompted in chat: stricter, so the bot stays quiet unless it is sure. */
export const isSure = (r) => !!r && (r.score >= 9.0 || (r.hits >= 3 && r.hits === r.of && r.score >= 7));
