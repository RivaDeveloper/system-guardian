// Slash commands. /ask is for everyone; the rest need the Moderate Members permission (and the bot checks again when run).
import { SlashCommandBuilder, PermissionFlagsBits, EmbedBuilder } from 'discord.js';
import { escalate, STRIKE_WINDOW_DAYS } from './escalation.js';
import { clip } from './util.js';

const mod = PermissionFlagsBits.ModerateMembers;

export const definitions = [
  new SlashCommandBuilder().setName('ask').setDescription('Ask a question about Lumen, Lumen Studios or Guardian')
    .addStringOption((o) => o.setName('question').setDescription('Your question').setRequired(true).setMaxLength(300)),
  new SlashCommandBuilder().setName('warn').setDescription('Give a member a strike (moderators)').setDefaultMemberPermissions(mod)
    .addUserOption((o) => o.setName('member').setDescription('Who').setRequired(true))
    .addStringOption((o) => o.setName('reason').setDescription('Why').setRequired(true).setMaxLength(200)),
  new SlashCommandBuilder().setName('warnings').setDescription('Show a member\'s strikes (moderators)').setDefaultMemberPermissions(mod)
    .addUserOption((o) => o.setName('member').setDescription('Who').setRequired(true)),
  new SlashCommandBuilder().setName('clearwarnings').setDescription('Clear all of a member\'s strikes (moderators)').setDefaultMemberPermissions(mod)
    .addUserOption((o) => o.setName('member').setDescription('Who').setRequired(true)),
  new SlashCommandBuilder().setName('timeout').setDescription('Time a member out (moderators)').setDefaultMemberPermissions(mod)
    .addUserOption((o) => o.setName('member').setDescription('Who').setRequired(true))
    .addIntegerOption((o) => o.setName('minutes').setDescription('How long (0 removes a timeout)').setRequired(true).setMinValue(0).setMaxValue(40320))
    .addStringOption((o) => o.setName('reason').setDescription('Why').setMaxLength(200)),
  new SlashCommandBuilder().setName('purge').setDescription('Delete the latest messages in this channel (moderators)').setDefaultMemberPermissions(PermissionFlagsBits.ManageMessages)
    .addIntegerOption((o) => o.setName('count').setDescription('How many (1 to 100)').setRequired(true).setMinValue(1).setMaxValue(100)),
  new SlashCommandBuilder().setName('slowmode').setDescription('Set slowmode in this channel (moderators)').setDefaultMemberPermissions(PermissionFlagsBits.ManageChannels)
    .addIntegerOption((o) => o.setName('seconds').setDescription('0 turns it off').setRequired(true).setMinValue(0).setMaxValue(21600)),
].map((c) => c.toJSON());

export async function handleCommand(interaction, { store, moderator, answerer, resolver, cfg }) {
  const name = interaction.commandName;
  const reply = (content, ephemeral = true) => interaction.reply({ content, ephemeral, allowedMentions: { parse: [] } });

  if (name === 'ask') {
    await interaction.deferReply();
    const q = interaction.options.getString('question', true);
    const res = await answerer.answer(q, { userId: interaction.user.id });
    if (!res.text) return interaction.editReply({ content: answerer.noAnswerText(resolver.render, resolver.channelIds), allowedMentions: { parse: [] } });
    return interaction.editReply({ embeds: [answerer.toEmbed(res, resolver.render, resolver.channelIds)], allowedMentions: { parse: [] } });
  }

  // everything below is for staff
  const allowed = interaction.memberPermissions?.has(mod) || moderator.isStaff(interaction.member);
  if (!allowed) return reply('This command is for moderators.');
  const target = interaction.options.getUser('member');
  const member = target ? await interaction.guild.members.fetch(target.id).catch(() => null) : null;
  if (target && moderator.isStaff(member)) return reply('That member is staff, so I leave them alone.');
  if (target?.bot) return reply('That is a bot.');

  if (name === 'warn') {
    const reason = interaction.options.getString('reason', true);
    const strikes = store.addStrike({ userId: target.id, moderatorId: interaction.user.id, rule: 'manual', reason });
    const step = escalate(strikes, 'medium');
    if (step.timeoutMs && member) await member.timeout(step.timeoutMs, `Strike from ${interaction.user.tag}: ${reason}`).catch(() => {});
    await target.send(`You received a warning in **${interaction.guild.name}**: ${reason}\nThis is strike ${strikes} in ${STRIKE_WINDOW_DAYS} days (${step.label}).`).catch(() => {});
    await moderator.logChannel(interaction.guild).then((ch) => ch?.send({ embeds: [new EmbedBuilder().setColor(0xffb84a).setTitle('Manual warning').setDescription(`<@${target.id}> · ${reason}\nBy <@${interaction.user.id}> · strike ${strikes} · ${step.label}`)], allowedMentions: { parse: [] } }));
    return reply(`Warned <@${target.id}> (strike ${strikes}, ${step.label}).`);
  }
  if (name === 'warnings') {
    const list = store.list(target.id);
    if (!list.length) return reply(`<@${target.id}> has no strikes.`);
    const lines = list.map((s) => `• <t:${Math.floor(s.at / 1000)}:d> **${s.rule}** ${clip(s.reason, 120)}`).join('\n');
    return reply(`**${store.countRecent(target.id)}** strike(s) in the last ${STRIKE_WINDOW_DAYS} days for <@${target.id}>:\n${lines}`);
  }
  if (name === 'clearwarnings') return reply(`Cleared ${store.clear(target.id)} strike(s) for <@${target.id}>.`);
  if (name === 'timeout') {
    if (!member) return reply('I can\'t find that member.');
    const minutes = interaction.options.getInteger('minutes', true);
    const reason = interaction.options.getString('reason') || 'No reason given';
    await member.timeout(minutes ? minutes * 60_000 : null, `${interaction.user.tag}: ${reason}`).catch((e) => { throw new Error(`Discord refused: ${e.message}`); });
    return reply(minutes ? `Timed out <@${target.id}> for ${minutes} minute(s).` : `Removed the timeout from <@${target.id}>.`);
  }
  if (name === 'purge') {
    const count = interaction.options.getInteger('count', true);
    const deleted = await interaction.channel.bulkDelete(count, true).catch(() => null);
    return reply(deleted ? `Deleted ${deleted.size} message(s). Messages older than 14 days can't be bulk deleted.` : 'I couldn\'t delete messages here.');
  }
  if (name === 'slowmode') {
    const seconds = interaction.options.getInteger('seconds', true);
    await interaction.channel.setRateLimitPerUser(seconds);
    return reply(seconds ? `Slowmode is now ${seconds}s.` : 'Slowmode is off.');
  }
}
