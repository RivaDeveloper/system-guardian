// Applies the rules from rules.js to real Discord messages. The decisions are pure (rules.js, escalation.js); this file only
// gathers facts from a message and carries out the result, so it stays small.
import { EmbedBuilder, PermissionFlagsBits } from 'discord.js';
import { evaluate, FloodTracker } from './rules.js';
import { escalate, STRIKE_WINDOW_DAYS } from './escalation.js';
import { plain, clip } from './util.js';

const COLORS = { low: 0xffb84a, medium: 0xff7a3d, high: 0xff2e55 };
const NOTICE_SECONDS = 12;

export class Moderator {
  constructor({ store, cfg, client }) {
    this.store = store; this.cfg = cfg; this.client = client;
    this.flood = new FloodTracker();
    this.logChannelId = null;
  }

  isStaff(member) {
    if (!member) return false;
    if (member.permissions?.has(PermissionFlagsBits.ManageMessages) || member.permissions?.has(PermissionFlagsBits.Administrator)) return true;
    return member.roles?.cache?.some((r) => this.cfg.staffRoles.includes(r.name)) ?? false;
  }

  async logChannel(guild) {
    if (this.logChannelId) return guild.channels.cache.get(this.logChannelId);
    const ch = guild.channels.cache.find((c) => plain(c.name) === plain(this.cfg.modLogChannel) && c.isTextBased());
    this.logChannelId = ch?.id ?? null;
    return ch;
  }

  /** Returns what it did, for tests and for the log. */
  async handle(message) {
    if (!message.guild || message.author?.bot || message.webhookId || message.system) return null;
    if (this.cfg.ignoreChannels.includes(plain(message.channel?.name ?? ''))) return null;
    const member = message.member ?? (await message.guild.members.fetch(message.author.id).catch(() => null));
    if (this.isStaff(member)) return null;

    const facts = {
      content: message.content ?? '',
      attachmentNames: [...(message.attachments?.values?.() ?? [])].map((a) => a.name ?? ''),
      mentionCount: (message.mentions?.users?.size ?? 0) + (message.mentions?.roles?.size ?? 0),
      mentionsEveryone: !!message.mentions?.everyone,
      accountAgeDays: message.author.createdTimestamp ? (Date.now() - message.author.createdTimestamp) / 86400_000 : null,
      minutesSinceJoin: member?.joinedTimestamp ? (Date.now() - member.joinedTimestamp) / 60_000 : null,
    };
    const findings = evaluate(facts, { allowedInvites: this.cfg.allowedInvites, mentionLimit: this.cfg.mentionLimit });
    const burst = this.flood.record(message.author.id, facts.content);
    if (burst) findings.push(burst);
    if (!findings.length) return null;
    return this.apply(message, member, findings);
  }

  async apply(message, member, findings) {
    const dry = this.cfg.dryRun;
    const worst = findings.slice().sort((a, b) => ({ low: 1, medium: 2, high: 3 })[b.severity] - ({ low: 1, medium: 2, high: 3 })[a.severity])[0];
    const countsAsStrike = findings.some((f) => f.severity !== 'low');
    const result = { rules: findings.map((f) => f.rule), deleted: false, timeoutMs: 0, strikes: this.store.countRecent(message.author.id, STRIKE_WINDOW_DAYS), askStaff: false, dry };

    if (findings.some((f) => f.delete) && !dry) result.deleted = await message.delete().then(() => true, () => false);
    else if (findings.some((f) => f.delete)) result.deleted = 'would';

    let label = 'a note';
    if (countsAsStrike) {
      result.strikes = dry ? result.strikes + 1 : this.store.addStrike({ userId: message.author.id, rule: worst.rule, reason: worst.reason });
      const step = escalate(result.strikes, worst.severity);
      label = step.label; result.timeoutMs = step.timeoutMs; result.askStaff = step.askStaff;
      if (step.timeoutMs && !dry) await member?.timeout(step.timeoutMs, `Lumen bot: ${worst.reason}`).catch((e) => { result.timeoutError = e.message; });
    }

    // a short public note that removes itself, and a private DM with the details
    const reasons = [...new Set(findings.map((f) => f.reason))].join('; ');
    if (!dry) {
      const note = await message.channel.send({ content: `<@${message.author.id}> I removed your message: ${clip(reasons, 300)}.`, allowedMentions: { users: [message.author.id] } }).catch(() => null);
      if (note) setTimeout(() => note.delete().catch(() => {}), NOTICE_SECONDS * 1000);
      const secret = findings.find((f) => f.secret);
      const dm = secret
        ? `I removed a message of yours in **${message.guild.name}** because it contained a key or token. Treat it as leaked: **create a new one in the service's settings right away**, then update it wherever you use it. No one here will ever ask you for a key.`
        : `Your message in **${message.guild.name}** was removed: ${reasons}.\nThis counts as ${label}. Please read the server rules; if you think this was a mistake, ask a moderator.`;
      await message.author.send(dm).catch(() => {});
    }

    await this.log(message, findings, worst, result, label);
    return result;
  }

  async log(message, findings, worst, result, label) {
    const ch = await this.logChannel(message.guild);
    if (!ch) return;
    const secret = findings.some((f) => f.secret);
    const embed = new EmbedBuilder()
      .setColor(COLORS[worst.severity])
      .setTitle(`${result.dry ? '[dry run] ' : ''}${result.rules.join(', ')}`)
      .setDescription(clip(findings.map((f) => `• ${f.reason}`).join('\n'), 800))
      .addFields(
        { name: 'Member', value: `<@${message.author.id}> (${message.author.tag ?? message.author.username})`, inline: true },
        { name: 'Channel', value: `<#${message.channel.id}>`, inline: true },
        { name: 'Action', value: `${result.deleted ? 'message removed, ' : ''}${label}${result.timeoutMs ? '' : ''} · strikes in ${STRIKE_WINDOW_DAYS} days: ${result.strikes}`, inline: false },
      )
      .setTimestamp();
    // the text of a leaked key is never copied anywhere
    if (!secret && message.content) embed.addFields({ name: 'Message', value: '```\n' + clip(message.content.replace(/```/g, "'''"), 400) + '\n```' });
    const staffRole = message.guild.roles.cache.find((r) => this.cfg.staffRoles.includes(r.name) && r.mentionable);
    const ping = result.askStaff && staffRole ? `<@&${staffRole.id}> needs a human decision.` : undefined;
    await ch.send({ content: ping, embeds: [embed], allowedMentions: { roles: staffRole ? [staffRole.id] : [] } }).catch(() => {});
  }
}
