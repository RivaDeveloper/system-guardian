// The moderation rules, as pure functions: a message goes in, a list of findings comes out. Nothing here talks to Discord,
// so every rule can be tested with plain strings. moderation.js turns findings into actions.
//
// Each finding: { rule, reason, severity: 'low' | 'medium' | 'high', delete: boolean, secret?: boolean }
//   low     a gentle correction (warn, count a strike)
//   medium  delete and count a strike
//   high    likely a scam or a compromised account: delete, timeout, and ask the staff to look

const BLOCKED_EXTENSIONS = new Set([
  'exe', 'scr', 'bat', 'cmd', 'com', 'pif', 'msi', 'msix', 'vbs', 'vbe', 'js', 'jse', 'wsf', 'hta', 'jar', 'ps1', 'psm1', 'lnk', 'dll', 'sys',
  'reg', 'apk', 'zip', 'rar', '7z', 'iso', 'img', 'cab', 'gz', 'tar',
]);

// strings that look like keys or tokens. The matched text is never stored or logged.
const SECRET_PATTERNS = [
  ['a Discord bot token', /\b[MNO][A-Za-z\d_-]{23,27}\.[A-Za-z\d_-]{6}\.[A-Za-z\d_-]{27,}\b/],
  ['a GitHub token', /\bgh[pousr]_[A-Za-z0-9]{36,}\b/],
  ['an Anthropic or OpenAI key', /\bsk-(?:ant-)?[A-Za-z0-9_-]{32,}\b/],
  ['an AWS access key', /\bAKIA[0-9A-Z]{16}\b/],
  ['a Real-Debrid style API key', /\b[A-Z0-9]{52}\b/],
  // addon links usually carry the user's own key inside the path: /<long token>/manifest.json
  ['an addon link that contains a key', /https?:\/\/[^\s/]+\/(?:[^\s/]*\/)*[A-Za-z0-9%=_-]{40,}(?:\/[^\s/]*)*\/manifest\.json/i],
  ['a private key block', /-----BEGIN [A-Z ]*PRIVATE KEY-----/],
];

const SCAM_PHRASES = [
  /free\s+nitro/i, /discord\.gift\//i, /steam\s+gift/i, /claim\s+your\s+(?:prize|reward|gift)/i, /\bairdrop\b/i,
  /discord(?:app)?[-.](?:gifts?|nitro)\./i, /\bnitro\s+(?:for\s+free|giveaway)\b/i, /you(?:'ve| have)\s+been\s+(?:selected|chosen)/i,
];

// narrow on purpose: a question like "is Lumen a pirate site?" must not be removed
const PIRACY_PATTERNS = [
  ['a magnet link', /magnet:\?xt=/i],
  ['a piracy site', /\b(?:1337x|rarbg|yts\.mx|fmovies|123movies|kissanime|putlocker|thepiratebay|piratebay|nyaa\.si)\b/i],
  ['cracked software', /\b(?:cracked\s+(?:version|apk|app|software|game)|crack\s+for\b|keygen|nulled)\b/i],
  ['a request for free copies', /\b(?:where|how)\b.{0,30}\b(?:watch|download|get)\b.{0,30}\bfor\s+free\b/i],
];

const INVITE = /(?:discord(?:app)?\.com\/invite|discord\.gg)\/([A-Za-z0-9-]+)/gi;
const URL_ANY = /https?:\/\/[^\s<>)]+/gi;

export function extractUrls(text) { return text.match(URL_ANY) || []; }

/**
 * @param {object} m
 * @param {string} m.content
 * @param {string[]} m.attachmentNames
 * @param {number} m.mentionCount      users + roles mentioned
 * @param {boolean} m.mentionsEveryone
 * @param {number|null} m.accountAgeDays
 * @param {number|null} m.minutesSinceJoin
 * @param {object} opts
 * @param {string[]} [opts.allowedInvites]   invite codes that are fine (the server's own)
 * @param {number} [opts.mentionLimit]
 */
export function evaluate(m, opts = {}) {
  const content = m.content || '';
  const findings = [];
  const add = (f) => findings.push({ delete: true, severity: 'medium', ...f });

  for (const [what, re] of SECRET_PATTERNS) {
    if (re.test(content)) add({ rule: 'secret', reason: `The message contained ${what}`, severity: 'medium', secret: true });
  }

  if (SCAM_PHRASES.some((re) => re.test(content))) {
    add({ rule: 'scam', reason: 'Looks like a scam message (free gifts, nitro, airdrops)', severity: 'high' });
  }

  for (const [what, re] of PIRACY_PATTERNS) {
    if (re.test(content)) { add({ rule: 'piracy', reason: `Rule 3 (no piracy): ${what}`, severity: 'medium' }); break; }
  }

  const allowed = new Set((opts.allowedInvites || []).map((c) => c.toLowerCase()));
  for (const match of content.matchAll(INVITE)) {
    if (!allowed.has(match[1].toLowerCase())) { add({ rule: 'invite', reason: 'Rule 7 (no unsolicited invites)', severity: 'medium' }); break; }
  }

  const bad = (m.attachmentNames || []).filter((n) => BLOCKED_EXTENSIONS.has(String(n).split('.').pop().toLowerCase()));
  if (bad.length) {
    add({ rule: 'attachment', reason: `Files that can hold programs aren't allowed here (${bad.map((n) => n.split('.').pop().toLowerCase()).join(', ')}). Share a hash or a link to the official GitHub release instead`, severity: 'low' });
  }

  const limit = opts.mentionLimit ?? 5;
  if ((m.mentionCount || 0) > limit || m.mentionsEveryone) {
    add({ rule: 'mentions', reason: m.mentionsEveryone ? 'Tried to mention everyone' : `Mentioned ${m.mentionCount} people in one message`, severity: 'medium' });
  }

  // fresh account or just-joined member dropping links is the classic spam pattern
  if (extractUrls(content).length && ((m.minutesSinceJoin != null && m.minutesSinceJoin < 10) || (m.accountAgeDays != null && m.accountAgeDays < 2))
      && !findings.length) {
    add({ rule: 'new-link', reason: 'A link from a brand-new member or account', severity: 'low' });
  }

  return findings;
}

/** Remembers recent messages per user to spot floods and repeats. In memory only; nothing is written to disk. */
export class FloodTracker {
  constructor({ windowMs = 8000, maxInWindow = 7, repeatWindowMs = 30000, maxRepeats = 3 } = {}) {
    Object.assign(this, { windowMs, maxInWindow, repeatWindowMs, maxRepeats });
    this.recent = new Map();
  }

  /** Returns a finding when this message tips the user over a limit, else null. */
  record(userId, content, now = Date.now()) {
    const list = (this.recent.get(userId) || []).filter((e) => now - e.at < this.repeatWindowMs);
    list.push({ at: now, content: content.trim().toLowerCase() });
    this.recent.set(userId, list);
    if (this.recent.size > 5000) for (const [k, v] of this.recent) if (!v.length || now - v[v.length - 1].at > 60000) this.recent.delete(k);

    const burst = list.filter((e) => now - e.at < this.windowMs).length;
    if (burst > this.maxInWindow) return { rule: 'flood', reason: `Sent ${burst} messages in a few seconds`, severity: 'medium', delete: true };
    const same = list.filter((e) => e.content && e.content === list[list.length - 1].content).length;
    if (content.trim().length > 4 && same >= this.maxRepeats) return { rule: 'repeat', reason: 'Repeated the same message', severity: 'medium', delete: true };
    return null;
  }
}

export const strictest = (findings) => findings.reduce((a, f) => ({ low: 1, medium: 2, high: 3 })[f.severity] > ({ low: 1, medium: 2, high: 3 })[a] ? f.severity : a, 'low');
