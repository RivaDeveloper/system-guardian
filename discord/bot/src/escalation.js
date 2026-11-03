// What happens after a violation, from how many strikes someone already has. Pure and testable. The bot never bans:
// the strongest thing it does by itself is a timeout, and for repeat offenders it asks a human.

const MIN = 60 * 1000;

/**
 * @param {number} strikesInWindow  strikes counting this one (last 30 days)
 * @param {'low'|'medium'|'high'} severity
 * @returns {{ timeoutMs: number, label: string, askStaff: boolean }}
 */
export function escalate(strikesInWindow, severity) {
  if (severity === 'high') return { timeoutMs: 24 * 60 * MIN, label: 'timed out for 1 day (looks like a scam or a hacked account)', askStaff: true };
  if (strikesInWindow <= 1) return { timeoutMs: 0, label: 'a warning', askStaff: false };
  if (strikesInWindow === 2) return { timeoutMs: 10 * MIN, label: 'timed out for 10 minutes', askStaff: false };
  if (strikesInWindow === 3) return { timeoutMs: 60 * MIN, label: 'timed out for 1 hour', askStaff: false };
  if (strikesInWindow === 4) return { timeoutMs: 24 * 60 * MIN, label: 'timed out for 1 day', askStaff: true };
  return { timeoutMs: 7 * 24 * 60 * MIN, label: 'timed out for 7 days', askStaff: true };
}

export const STRIKE_WINDOW_DAYS = 30;
