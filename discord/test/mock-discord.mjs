// A tiny in-memory stand-in for the parts of Discord's REST API that setup.mjs uses, so the script's logic
// (ordering, reuse on a second run, fallbacks) can be tested without a real server or token.
//   node test/mock-discord.mjs [port] [--no-community]
import http from 'node:http';

const port = Number(process.argv[2] || 8799);
const noCommunity = process.argv.includes('--no-community');
let nextId = 1000;
const id = () => String(nextId++);
const guild = { id: 'G1', name: 'old name', features: [], verification_level: 0 };
const roles = [{ id: 'G1', name: '@everyone', permissions: '0' }];
const channels = [];
const messages = {};       // channelId -> [msg]
const emojis = [];
const threads = [];
const automod = [];
const webhooks = {};       // channelId -> [hook]
const log = [];

const send = (res, code, body) => { res.writeHead(code, { 'Content-Type': 'application/json' }); res.end(body === undefined ? '' : JSON.stringify(body)); };

http.createServer((req, res) => {
  let raw = '';
  req.on('data', (c) => (raw += c));
  req.on('end', () => {
    const body = raw ? JSON.parse(raw) : {};
    const url = new URL(req.url, 'http://x');
    const path = url.pathname.replace(/^\/api\/v10/, '');
    log.push(`${req.method} ${path}`);
    let m;
    if (path === '/__state') return send(res, 200, { guild, roles, channels, messages, emojis, automod, webhooks, log, threads });
    if (req.headers.authorization !== 'Bot test-token') return send(res, 401, { message: '401: Unauthorized' });

    if (path === '/guilds/G1/threads/active') return send(res, 200, { threads });
    if ((m = path.match(/^\/channels\/(\d+)\/threads$/)) && req.method === 'POST') {
      const parent = channels.find((c) => c.id === m[1]);
      if (!parent || parent.type !== 15) return send(res, 400, { message: 'threads can only be started in forum channels here' });
      if (!body.message?.embeds?.length) return send(res, 400, { message: 'a forum post needs a message' });
      const t = { id: id(), parent_id: parent.id, name: body.name, applied_tags: body.applied_tags || [], flags: 0 };
      threads.push(t); messages[t.id] = [{ id: id(), author: { id: 'BOT1' }, ...body.message }]; return send(res, 200, t);
    }
    if ((m = path.match(/^\/channels\/(\d+)$/)) && req.method === 'GET') { const c = channels.find((x) => x.id === m[1]); return c ? send(res, 200, c) : send(res, 404, { message: 'Unknown Channel' }); }
    if ((m = path.match(/^\/channels\/(\d+)$/)) && req.method === 'PATCH' && threads.some((t) => t.id === m[1])) { Object.assign(threads.find((t) => t.id === m[1]), body); return send(res, 200, {}); }
    if (path === '/users/@me' && req.method === 'GET') return send(res, 200, { id: 'BOT1' });
    if ((m = path.match(/^\/channels\/(\d+)\/messages\/(\d+)$/)) && req.method === 'DELETE') { messages[m[1]] = (messages[m[1]] || []).filter((x) => x.id !== m[2]); return send(res, 204); }
    if (path === '/guilds/G1' && req.method === 'GET') return send(res, 200, guild);
    if (path === '/guilds/G1' && req.method === 'PATCH') {
      if (body.features?.includes('COMMUNITY') && noCommunity) return send(res, 400, { code: 50035, message: 'Community requirements not met' });
      if (body.icon && !String(body.icon).startsWith('data:image/png;base64,')) return send(res, 400, { message: 'bad icon' });
      if ((body.banner || body.splash)) return send(res, 400, { code: 30000, message: 'Server needs more boosts' });
      Object.assign(guild, body); return send(res, 200, guild);
    }
    if (path === '/guilds/G1/channels' && req.method === 'PATCH') { for (const o of body) { const c = channels.find((x) => x.id === o.id); if (c) { c.position = o.position; if (o.parent_id) c.parent_id = o.parent_id; } } return send(res, 204); }
    if (path === '/guilds/G1/roles' && req.method === 'GET') return send(res, 200, roles);
    if (path === '/guilds/G1/roles' && req.method === 'POST') { const r = { id: id(), ...body }; roles.push(r); return send(res, 200, r); }
    if (path === '/guilds/G1/roles' && req.method === 'PATCH') return send(res, 200, roles);
    if ((m = path.match(/^\/guilds\/G1\/roles\/(\d+)$/)) && req.method === 'PATCH') { Object.assign(roles.find((r) => r.id === m[1]), body); return send(res, 200, {}); }
    if (path === '/guilds/G1/channels' && req.method === 'GET') return send(res, 200, channels);
    if (path === '/guilds/G1/channels' && req.method === 'POST') {
      if (body.type === 15 && !guild.features.includes('COMMUNITY')) return send(res, 400, { message: 'Forum channels need Community' });
      const c = { id: id(), ...body }; channels.push(c); messages[c.id] = []; return send(res, 200, c);
    }
    if ((m = path.match(/^\/channels\/(\d+)$/)) && req.method === 'PATCH') {
      const c = channels.find((x) => x.id === m[1]); if (body.type === 5 && !guild.features.includes('COMMUNITY')) return send(res, 400, { message: 'needs Community' });
      Object.assign(c, Object.fromEntries(Object.entries(body).filter(([, v]) => v !== undefined))); return send(res, 200, c);
    }
    if ((m = path.match(/^\/channels\/(\d+)\/messages$/))) {
      if (req.method === 'GET') return send(res, 200, (messages[m[1]] || []).slice(-50));
      const msg = { id: id(), author: { id: 'BOT1' }, ...body }; (messages[m[1]] ||= []).push(msg); return send(res, 200, msg);
    }
    if (path.match(/^\/channels\/\d+\/pins\/\d+$/) && req.method === 'PUT') return send(res, 204);
    if ((m = path.match(/^\/channels\/(\d+)\/invites$/)) && req.method === 'POST') return send(res, 200, { code: 'abc123' });
    if ((m = path.match(/^\/channels\/(\d+)\/webhooks$/))) {
      if (req.method === 'GET') return send(res, 200, webhooks[m[1]] || []);
      const h = { id: id(), token: 'tok' + id(), ...body }; (webhooks[m[1]] ||= []).push(h); return send(res, 200, h);
    }
    if (path === '/guilds/G1/auto-moderation/rules' && req.method === 'GET') return send(res, 200, automod);
    if (path === '/guilds/G1/auto-moderation/rules' && req.method === 'POST') { const r = { id: id(), ...body }; automod.push(r); return send(res, 200, r); }
    if (path === '/guilds/G1/emojis' && req.method === 'GET') return send(res, 200, emojis);
    if (path === '/guilds/G1/emojis' && req.method === 'POST') { const e = { id: id(), ...body }; emojis.push(e); return send(res, 200, e); }
    return send(res, 404, { message: 'unknown route in the mock: ' + req.method + ' ' + path });
  });
}).listen(port, '127.0.0.1', () => console.log('mock discord on ' + port));
