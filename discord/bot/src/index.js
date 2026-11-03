// The Lumen server bot: moderation (rules.js) and answers from the server's own guides (knowledge.js, optionally phrased by Claude).
//   npm start            run the bot
//   npm start -- --check log in, show what the bot can see and do, then exit (nothing is posted or changed)
import { Client, GatewayIntentBits, Partials, Events, EmbedBuilder, REST, Routes, ChannelType, PermissionFlagsBits } from 'discord.js';
import { loadConfig } from './config.js';
import { Store } from './store.js';
import { Moderator } from './moderation.js';
import { Answerer } from './answer.js';
import { definitions, handleCommand } from './commands.js';
import { makeResolver, plain, clip } from './util.js';

const cfg = loadConfig();
const checkOnly = process.argv.includes('--check');
if (!cfg.token || !cfg.guildId) {
  console.error('Set DISCORD_TOKEN and GUILD_ID (see .env.example).');
  process.exit(1);
}

const client = new Client({
  intents: [GatewayIntentBits.Guilds, GatewayIntentBits.GuildMessages, GatewayIntentBits.MessageContent, GatewayIntentBits.GuildMembers],
  partials: [Partials.Message, Partials.Channel],
});
const store = new Store(cfg.dbFile);
const moderator = new Moderator({ store, cfg, client });
const answerer = new Answerer({ cfg });
let resolver = null;
const joins = [];                              // join timestamps, to spot a raid
let lastRaidAlert = 0;

const say = (...a) => console.log(new Date().toISOString().slice(11, 19), ...a);

client.once(Events.ClientReady, async () => {
  const guild = await client.guilds.fetch(cfg.guildId).catch(() => null);
  if (!guild) { console.error('The bot is not in that server (or GUILD_ID is wrong).'); process.exit(1); }
  await guild.channels.fetch(); await guild.roles.fetch(); await guild.members.fetchMe();
  resolver = makeResolver(guild, cfg.server);
  const me = guild.members.me;
  const need = ['ViewChannel', 'SendMessages', 'EmbedLinks', 'ReadMessageHistory', 'ManageMessages', 'ModerateMembers', 'SendMessagesInThreads'];
  const missing = need.filter((p) => !me.permissions.has(PermissionFlagsBits[p]));
  say(`Online as ${client.user.tag} in "${guild.name}". Answers from ${answerer.kb.docs.length} guide pieces; AI ${cfg.aiEnabled ? 'on' : 'off'}; ${cfg.dryRun ? 'DRY RUN (nothing is enforced)' : 'enforcing'}.`);
  if (missing.length) say('Missing permissions:', missing.join(', '));
  if (!(await moderator.logChannel(guild))) say(`No #${cfg.modLogChannel} channel found: moderation will work, but nothing is logged.`);

  const rest = new REST({ version: '10' }).setToken(cfg.token);
  if (!checkOnly) await rest.put(Routes.applicationGuildCommands(client.user.id, cfg.guildId), { body: definitions }).then(() => say('Slash commands registered.'), (e) => say('Could not register slash commands:', e.message));
  if (checkOnly) { say('Check finished. Nothing was posted or changed.'); await client.destroy(); store.close(); process.exit(0); }
});

// ---- moderation --------------------------------------------------------------------------------------------------------
const moderate = async (m) => { try { return await moderator.handle(m.partial ? await m.fetch() : m); } catch (e) { say('moderation error:', e.message); return null; } };

client.on(Events.MessageCreate, async (message) => {
  const acted = await moderate(message);
  if (acted?.deleted === true || message.author.bot || !message.guild || !resolver) return;
  try {
    // the ask-the-bot channel: every message is a question (answered even in dry run, since it only replies)
    if (answerer.shouldAnswerAskChannel(message)) {
      await message.channel.sendTyping().catch(() => {});
      const res = await answerer.answer(message.content.slice(0, 300), { userId: message.author.id });
      return void message.reply({ ...(res.text ? { embeds: [answerer.toEmbed(res, resolver.render, resolver.channelIds)] } : { content: answerer.noAnswerText(resolver.render, resolver.channelIds) }), allowedMentions: { repliedUser: false } });
    }
    // mention the bot to ask a question
    if (message.mentions.has(client.user) && !message.mentions.everyone) {
      const question = message.content.replace(/<@!?\d+>/g, '').trim();
      if (!question) return;
      if (cfg.dryRun) return say(`[dry run] would answer: ${clip(question, 80)}`);
      await message.channel.sendTyping().catch(() => {});
      const res = await answerer.answer(question, { userId: message.author.id });
      return void message.reply({ ...(res.text ? { embeds: [answerer.toEmbed(res, resolver.render, resolver.channelIds)] } : { content: answerer.noAnswerText(resolver.render, resolver.channelIds) }), allowedMentions: { repliedUser: false } });
    }
    // answer a clear question without being asked, only when sure
    const auto = answerer.considerAutoAnswer(message);
    if (auto && !cfg.dryRun) await message.reply({ embeds: [answerer.toEmbed(auto, resolver.render, resolver.channelIds)], allowedMentions: { repliedUser: false } });
    else if (auto) say(`[dry run] would answer unprompted: ${auto.title}`);
  } catch (e) { say('answer error:', e.message); }
});

client.on(Events.MessageUpdate, (_old, message) => { if (message.content != null) moderate(message); });

// ---- a friendly first reply under new forum posts ----------------------------------------------------------------------------------
client.on(Events.ThreadCreate, async (thread, newlyCreated) => {
  try {
    if (!newlyCreated || !cfg.forumAnswers || !resolver || thread.parent?.type !== ChannelType.GuildForum) return;
    const forum = plain(thread.parent.name);
    if (!cfg.forumChannels.includes(forum)) return;
    const starter = await thread.fetchStarterMessage().catch(() => null);
    if (!starter || starter.author.bot) return;
    const { best, checklist } = answerer.forumReply(forum, thread.name, starter.content ?? '');
    if (cfg.dryRun) return say(`[dry run] would greet forum post "${thread.name}" (${best ? 'with an answer' : 'checklist only'})`);
    const embeds = [];
    if (best) embeds.push(answerer.toEmbed({ text: best.text, title: best.title, source: best.source, usedAi: false }, resolver.render, resolver.channelIds).setAuthor({ name: 'This might already answer it' }));
    const lines = [`Thanks for the post! A person will look at it. To help them, make sure it says ${checklist}.`, 'Never post passwords, API keys or addon links (they contain your key).'];
    await thread.send({ content: lines.join('\n'), embeds, allowedMentions: { parse: [] } });
  } catch (e) { say('forum greeting error:', e.message); }
});

// ---- raids: a burst of joins is reported, never acted on automatically ------------------------------------------------------
client.on(Events.GuildMemberAdd, async (member) => {
  const now = Date.now();
  joins.push(now);
  while (joins.length && now - joins[0] > 60_000) joins.shift();
  if (joins.length >= 8 && now - lastRaidAlert > 10 * 60_000) {
    lastRaidAlert = now;
    const ch = await moderator.logChannel(member.guild);
    const staffRole = member.guild.roles.cache.find((r) => cfg.staffRoles.includes(r.name) && r.mentionable);
    await ch?.send({
      content: staffRole ? `<@&${staffRole.id}>` : undefined,
      embeds: [new EmbedBuilder().setColor(0xff2e55).setTitle('Join burst').setDescription(`${joins.length} members joined in the last minute. If this is a raid, raise the verification level in Server Settings → Safety Setup, or pause invites.`)],
      allowedMentions: { roles: staffRole ? [staffRole.id] : [] },
    }).catch(() => {});
  }
});

// ---- slash commands ----------------------------------------------------------------------------------------------------------------
client.on(Events.InteractionCreate, async (interaction) => {
  if (!interaction.isChatInputCommand() || !interaction.guild || !resolver) return;
  try { await handleCommand(interaction, { store, moderator, answerer, resolver, cfg }); }
  catch (e) {
    say('command error:', e.message);
    const body = { content: 'Something went wrong running that command.', ephemeral: true };
    if (interaction.deferred || interaction.replied) await interaction.followUp(body).catch(() => {}); else await interaction.reply(body).catch(() => {});
  }
});

client.on(Events.Error, (e) => say('client error:', e.message));
process.on('unhandledRejection', (e) => say('unhandled:', e?.message ?? e));
process.on('SIGINT', async () => { await client.destroy(); store.close(); process.exit(0); });

client.login(cfg.token).catch((e) => {
  if (/disallowed intents/i.test(e.message)) {
    console.error('\nDiscord refused the login: turn on **Message Content Intent** and **Server Members Intent** for this bot in the developer portal (Bot → Privileged Gateway Intents), then start the bot again.');
  } else console.error('Login failed:', e.message);
  process.exit(1);
});
