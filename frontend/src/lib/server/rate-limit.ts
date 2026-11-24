import "server-only";
import Redis from "ioredis";
import { env } from "./env";

let client: Redis | null | undefined;

function redis() {
  if (client !== undefined) return client;
  const url = env().REDIS_URL;
  client = url
    ? new Redis(url, {
        maxRetriesPerRequest: 1,
        enableOfflineQueue: false,
        lazyConnect: false,
      })
    : null;
  client?.on("error", () => {}); // reported by the fail-open path below, not as an unhandled event
  return client;
}

const memory = new Map<string, { count: number; resetAt: number }>();

/**
 * Fixed-window limiter for the BFF auth routes (§6.1: our own Redis-backed limiter on top of Supabase's).
 * Without REDIS_URL (local dev, tests) it counts in memory. If Redis is down it fails open: Supabase's own
 * auth rate limits still apply, and locking everyone out of sign-in is worse.
 */
export async function hit(key: string, limit: number, windowSeconds: number): Promise<{ allowed: boolean; retryAfter: number }> {
  const r = redis();
  if (r) {
    try {
      const k = `tc:bff:${key}`;
      const [[, count], [, ttl]] = (await r.multi().incr(k).ttl(k).exec()) as [[null, number], [null, number]];
      if (ttl < 0) await r.expire(k, windowSeconds);
      return {
        allowed: count <= limit,
        retryAfter: ttl > 0 ? ttl : windowSeconds,
      };
    } catch {
      return { allowed: true, retryAfter: 0 };
    }
  }
  const now = Date.now();
  const entry = memory.get(key);
  if (!entry || entry.resetAt <= now) {
    memory.set(key, { count: 1, resetAt: now + windowSeconds * 1000 });
    return { allowed: true, retryAfter: windowSeconds };
  }
  entry.count++;
  return {
    allowed: entry.count <= limit,
    retryAfter: Math.ceil((entry.resetAt - now) / 1000),
  };
}
