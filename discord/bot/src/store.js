// Strikes live in a small SQLite file (node's built-in sqlite, no extra install). Only what moderation needs is kept:
// who, why, when, which rule. Never message text, and never a leaked key.
import { DatabaseSync } from 'node:sqlite';
import { mkdirSync } from 'node:fs';
import { dirname } from 'node:path';

export class Store {
  constructor(file = 'data/bot.db') {
    if (file !== ':memory:') mkdirSync(dirname(file), { recursive: true });
    this.db = new DatabaseSync(file);
    this.db.exec(`CREATE TABLE IF NOT EXISTS strikes (
      id INTEGER PRIMARY KEY AUTOINCREMENT, user_id TEXT NOT NULL, moderator_id TEXT, rule TEXT NOT NULL,
      reason TEXT NOT NULL, created_at INTEGER NOT NULL, cleared INTEGER NOT NULL DEFAULT 0);
      CREATE INDEX IF NOT EXISTS strikes_user ON strikes (user_id, created_at);`);
  }

  addStrike({ userId, moderatorId = null, rule, reason, at = Date.now() }) {
    this.db.prepare('INSERT INTO strikes (user_id, moderator_id, rule, reason, created_at) VALUES (?, ?, ?, ?, ?)').run(userId, moderatorId, rule, reason, at);
    return this.countRecent(userId, 30, at);
  }

  countRecent(userId, days = 30, now = Date.now()) {
    const since = now - days * 86400_000;
    return Number(this.db.prepare('SELECT COUNT(*) AS n FROM strikes WHERE user_id = ? AND cleared = 0 AND created_at >= ?').get(userId, since).n);
  }

  list(userId, limit = 10) {
    return this.db.prepare('SELECT id, rule, reason, created_at AS at, moderator_id AS moderator FROM strikes WHERE user_id = ? AND cleared = 0 ORDER BY created_at DESC LIMIT ?').all(userId, limit);
  }

  clear(userId) {
    return Number(this.db.prepare('UPDATE strikes SET cleared = 1 WHERE user_id = ? AND cleared = 0').run(userId).changes);
  }

  close() { this.db.close(); }
}
