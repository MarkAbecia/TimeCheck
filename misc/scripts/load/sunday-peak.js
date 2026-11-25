// k6 load test for the v1 target (REQUIREMENTS §9): 300 people at once on a Sunday, p95 < 300 ms on core endpoints.
// Read-only, so it is safe against staging. Run against a stack seeded at scale (1,000+ members, a year of events).
//
//   k6 run -e API=https://api.staging.example -e SUPABASE=https://xyz.supabase.co -e ANON_KEY=... \
//          -e USERS=leader1@x.test,leader2@x.test,... -e PASSWORD=... misc/scripts/load/sunday-peak.js
//
// Sign-ins are rate-limited (Supabase: 30 per 5 min per IP; API: 300 requests per minute per user), so the test signs in a
// pool of up to 30 test accounts once and shares each token across VUs: 300 VUs on 30 accounts is ~200 requests per
// account per minute at the pacing below. Use leader accounts so the leader-only endpoints answer.
import http from "k6/http";
import { check, sleep } from "k6";

const API = __ENV.API ?? "http://localhost:5170";
const SUPABASE = __ENV.SUPABASE ?? "http://127.0.0.1:55421";

export const options = {
  scenarios: {
    sunday: {
      executor: "ramping-vus",
      stages: [
        { duration: "2m", target: 300 },
        { duration: "5m", target: 300 },
        { duration: "1m", target: 0 },
      ],
    },
  },
  thresholds: {
    "http_req_duration{core:yes}": ["p(95)<300"],
    http_req_failed: ["rate<0.01"],
  },
};

export function setup() {
  const users = (__ENV.USERS ?? "").split(",").filter(Boolean).slice(0, 30);
  if (users.length === 0) throw new Error("Set USERS to a comma-separated list of test leader emails");
  return users.map((email) => {
    const res = http.post(`${SUPABASE}/auth/v1/token?grant_type=password`, JSON.stringify({ email, password: __ENV.PASSWORD }), {
      headers: { "Content-Type": "application/json", apikey: __ENV.ANON_KEY },
    });
    if (res.status !== 200) throw new Error(`Sign-in failed for ${email}: ${res.status}`);
    return res.json("access_token");
  });
}

const iso = (d) => d.toISOString().slice(0, 10);

export default function (tokens) {
  const params = (core) => ({ headers: { Authorization: `Bearer ${tokens[__VU % tokens.length]}` }, tags: { core } });
  const today = new Date();
  const monthAgo = new Date(today.getTime() - 29 * 86_400_000);

  // What a leader's phone does on a Sunday: open Today, look at the week's events and one event's sheet, check notifications.
  const me = http.get(`${API}/api/v1/me`, params("yes"));
  check(me, { "me 200": (r) => r.status === 200 });
  http.get(`${API}/api/v1/me/today`, params("yes"));
  sleep(2);

  const events = http.get(`${API}/api/v1/events?from=${iso(today)}&to=${iso(new Date(today.getTime() + 6 * 86_400_000))}`, params("yes"));
  check(events, { "events 200": (r) => r.status === 200 });
  const first = events.status === 200 ? events.json("0.id") : null;
  if (first) http.get(`${API}/api/v1/events/${first}`, params("yes"));
  sleep(2);

  http.get(`${API}/api/v1/me/notifications`, params("yes"));
  sleep(2);

  // One in ten opens a month's attendance report (cached after the first build, so not counted as core).
  if (Math.random() < 0.1) {
    const report = http.get(`${API}/api/v1/reports/attendance?from=${iso(monthAgo)}&to=${iso(today)}`, params("no"));
    check(report, { "report 200": (r) => r.status === 200 });
  }
  sleep(3);
}
