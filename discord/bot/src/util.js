// Helpers for channel names and for the {placeholders} used in the server's written content.

/** "💬｜general" -> "general" */
export const plain = (name) => String(name).toLowerCase().replace(/^[^\p{L}\p{N}]+/u, '').replace(/^[｜|]+/u, '').replace(/[^\p{L}\p{N}]+/gu, '-').replace(/^-|-$/g, '');

/** Turns <#{key}> into a real channel mention and {link} into the website or download link. */
export function makeResolver(guild, server) {
  const byName = new Map();
  for (const c of guild?.channels?.cache?.values?.() ?? []) byName.set(plain(c.name), c.id);
  const channelIds = {};
  for (const cat of server.categories) for (const ch of cat.channels) {
    const id = byName.get(plain(ch.name)) || (ch.was || []).map((w) => byName.get(plain(w))).find(Boolean);
    if (id) channelIds[ch.key] = id;
  }
  const render = (text) => String(text)
    .replace(/<#\{(\w+)\}>/g, (_, k) => (channelIds[k] ? `<#${channelIds[k]}>` : `#${k}`))
    .replace(/\{(\w+)\}/g, (_, k) => server.links[k] ?? '')
    .replace(/:[a-z]+:/g, '');                       // custom-emoji tokens only make sense inside the server's own embeds
  return { render, channelIds };
}

export const clip = (text, n) => (text.length > n ? text.slice(0, n - 1).trimEnd() + '…' : text);
