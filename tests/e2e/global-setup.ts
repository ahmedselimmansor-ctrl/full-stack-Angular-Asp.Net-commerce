import { execSync } from 'node:child_process';

/**
 * Clears rate-limit counters before the suite runs.
 *
 * Every test registers its own account so nothing depends on state another test left behind.
 * That is deliberately the shape the registration limit exists to stop — many signups from
 * one address — so the counters are reset rather than the limit weakened.
 *
 * Only the rate-limit keys are removed. Flushing Redis wholesale would also destroy baskets
 * and idempotency records, which the suite is in the middle of relying on.
 */
export default function globalSetup(): void {
  try {
    const removed = execSync(
      `docker exec sama-redis redis-cli --no-raw eval ` +
      `"local ks = redis.call('keys', '*rl:*'); for i, k in ipairs(ks) do redis.call('del', k) end; return #ks" 0`,
      { encoding: 'utf8', stdio: ['ignore', 'pipe', 'ignore'] },
    ).trim();

    console.log(`[global-setup] cleared ${removed} rate-limit key(s)`);
  } catch {
    // Not fatal: the suite still runs, it may just hit a throttle partway through. Saying so
    // beats failing before a single test has had a chance.
    console.warn('[global-setup] could not reach Redis to clear rate limits; continuing');
  }
}
