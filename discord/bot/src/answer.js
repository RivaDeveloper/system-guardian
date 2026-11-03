// Answering questions: /ask, mentions, a greeting on new forum posts, and (carefully) a reply to clear questions in chat.
import { EmbedBuilder } from 'discord.js';
import { Knowledge, isConfident, isSure } from './knowledge.js';
import { askClaude, aiEnabled, AiBudget } from './ai.js';
import { plain, clip } from './util.js';

const COLOR = 0x8b6bff;
const QUESTION_START = /^(?:how|why|what|where|when|which|who|can|could|does|do|did|is|are|will|would|should|any(?:one|body)?\s+(?:know|have))\b/i;
const SOURCE_TO_CHANNEL = { releasesinfo: 'releases' };

export class Answerer {
  constructor({ cfg, kb = new Knowledge(), env = process.env, fetchImpl = fetch }) {
    this.cfg = cfg; this.kb = kb; this.env = env; this.fetchImpl = fetchImpl;
    this.budget = new AiBudget(Number(env.AI_PER_USER_PER_HOUR || 6));
    this.lastInChannel = new Map(); this.lastForUser = new Map(); this.lastAsk = new Map();
  }

  /**
   * @returns {{ text:string, title:string, source:string, confident:boolean, usedAi:boolean, related:string[] }}
   */
  async answer(question, { userId, allowAi = true } = {}) {
    const results = this.kb.search(question, 4);
    const best = results[0];
    const confident = isConfident(best);
    let text = null, usedAi = false;
    if (allowAi && aiEnabled(this.env) && best && this.budget.take(userId ?? 'anon')) {
      text = await askClaude(question, results.slice(0, 5), this.env, this.fetchImpl);
      usedAi = !!text;
    }
    if (!text && confident) text = best.text;
    return {
      text, usedAi, confident: confident || usedAi,
      title: best?.title ?? '', source: best?.source ?? '',
      related: results.slice(1, 3).filter((r) => r.score > best.score * 0.6 && r.title !== best.title).map((r) => r.title),
    };
  }

  /** The reply shown in Discord. `render` resolves channel placeholders (util.makeResolver). */
  toEmbed(res, render, channelIds) {
    const channelKey = SOURCE_TO_CHANNEL[res.source] ?? res.source;
    const where = channelIds?.[channelKey] ? ` · more in <#${channelIds[channelKey]}>` : '';
    const e = new EmbedBuilder().setColor(COLOR).setDescription(clip(render(res.text), 1800));
    if (!res.usedAi && res.title) e.setTitle(clip(render(res.title).replace(/^\d+ · /, ''), 200));
    e.setFooter({ text: res.usedAi ? 'Answered by the AI helper from the server guides. A person can still correct it.' : 'From the server guides. Not what you meant? Ask in the help forum.' });
    if (where) e.addFields({ name: '​', value: where.slice(3) });
    return e;
  }

  noAnswerText(render, channelIds) {
    const help = channelIds?.help ? `<#${channelIds.help}>` : 'the help forum';
    return `I'm not sure about that one. Make a post in ${help} with the app in the tags and a person will help.`;
  }

  // ---- unprompted replies in chat -------------------------------------------------------------------------------

  looksLikeQuestion(content) {
    const t = content.trim();
    if (t.length < 14 || t.length > 400 || /https?:\/\//.test(t)) return false;
    return /\?\s*$/.test(t) || QUESTION_START.test(t);
  }

  /** Decides whether to speak up in chat. Quiet by default: needs a clear question, a sure match, and no recent bot reply. */
  considerAutoAnswer(message, now = Date.now()) {
    if (!this.cfg.autoAnswer || message.author.bot || !message.guild) return null;
    if (!this.cfg.answerChannels.includes(plain(message.channel?.name ?? ''))) return null;
    if (message.reference?.messageId || message.mentions?.users?.size > 0) return null;           // part of a conversation between people
    if (!this.looksLikeQuestion(message.content)) return null;
    if (now - (this.lastInChannel.get(message.channel.id) ?? 0) < 90_000) return null;
    if (now - (this.lastForUser.get(message.author.id) ?? 0) < 10 * 60_000) return null;
    const best = this.kb.search(message.content, 1)[0];
    if (!isSure(best)) return null;
    this.lastInChannel.set(message.channel.id, now); this.lastForUser.set(message.author.id, now);
    return { text: best.text, title: best.title, source: best.source, usedAi: false, confident: true, related: [] };
  }

  // ---- the ask-the-bot channel: every message there is a question -----------------------------------------------------------

  /** True when this message should be answered because it is in the ask channel. One answer per person every 4 seconds. */
  shouldAnswerAskChannel(message, now = Date.now()) {
    if (message.author.bot || !message.guild) return false;
    if (plain(message.channel?.name ?? '') !== plain(this.cfg.askChannel ?? 'ask-the-bot')) return false;
    if ((message.content ?? '').trim().length < 3) return false;
    if (message.reference?.messageId) return false;                       // a person replying to a person
    if (now - (this.lastAsk.get(message.author.id) ?? 0) < 4000) return false;
    this.lastAsk.set(message.author.id, now);
    return true;
  }

  // ---- forum posts ----------------------------------------------------------------------------------------------------

  /** What to say under a new help/bug/idea post: likely answers when there are any, plus what a good report includes. */
  forumReply(forumName, title, body) {
    const res = this.kb.search(`${title} ${body}`, 3);
    const best = res[0];
    const checklist = { help: 'the app and its version, your Windows or device version, what you did, and the exact message you saw', 'bug-reports': 'the app and version, what you expected, what happened and how to repeat it', 'false-positives': 'the file name, publisher, where you got it, the SHA-256 and the "why it was flagged" text', 'feature-requests': 'the problem you want solved and how you work around it today' }[forumName];
    return { best: isConfident(best) ? best : null, checklist };
  }
}
