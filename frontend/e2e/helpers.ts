import { execFileSync } from "node:child_process";
import { createHmac } from "node:crypto";
import AxeBuilder from "@axe-core/playwright";
import { expect, type Page } from "@playwright/test";

// Seeded dev accounts (database/supabase/seed.sql). Local only.
export const PASSWORD = "TimeCheck-dev-123!";
export const LEADER = "leader@timecheck.test";
export const MEMBER = "member@timecheck.test";
/** Seeded test-only admin. Its authenticator is re-enrolled by every admin spec, so don't keep a real one on it. */
export const ADMIN = "admin@timecheck.test";
export const LEADER_STATE = "e2e/.auth/leader.json";

/** People the specs create use this domain, so teardown can find and remove exactly them. */
export const E2E_DOMAIN = "e2e.timecheck.test";
export const uniqueEmail = (name: string) => `${name}.${Date.now().toString(36)}${Math.random().toString(36).slice(2, 6)}@${E2E_DOMAIN}`;

/**
 * Runs SQL in the local Supabase database and returns psql's unaligned output. For test setup only.
 * shortcut: goes through `docker exec`; use a direct connection when e2e runs in CI.
 */
export const sql = (query: string) =>
  execFileSync(
    "docker",
    ["exec", "-i", process.env.E2E_DB_CONTAINER ?? "supabase_db_timecheck", "psql", "-U", "postgres", "-tAq", "-v", "ON_ERROR_STOP=1"],
    {
      input: query,
      encoding: "utf8",
    },
  ).trim();

const MAILPIT = process.env.MAILPIT_URL ?? "http://127.0.0.1:55424";

export async function signIn(page: Page, email: string, password = PASSWORD) {
  await page.goto("/login");
  await page.getByLabel("Email", { exact: true }).fill(email);
  await page.getByLabel("Password", { exact: true }).fill(password);
  await page.getByRole("button", { name: "Sign in" }).click();
}

/** RFC 6238 TOTP (SHA-1, 6 digits, 30 s) from a base32 secret: the code an authenticator app shows. */
export function totp(secret: string, at = Date.now()): string {
  const alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
  const bits = [...secret.replace(/[\s=]/g, "").toUpperCase()].map((c) => alphabet.indexOf(c).toString(2).padStart(5, "0")).join("");
  const key = Buffer.from((bits.match(/.{8}/g) ?? []).map((b) => parseInt(b, 2)));
  const counter = Buffer.alloc(8);
  counter.writeBigUInt64BE(BigInt(Math.floor(at / 30_000)));
  const h = createHmac("sha1", key).update(counter).digest();
  return String((h.readUInt32BE(h[h.length - 1] & 0xf) & 0x7fffffff) % 1_000_000).padStart(6, "0");
}

/**
 * Signs in the seeded admin with a fresh authenticator: removes any factor it had, enrols a new one through the MFA page
 * and answers with the code computed from the key shown there. Admins can't reach anything else without it (§6.1).
 */
export async function signInAsAdmin(page: Page) {
  sql(`delete from auth.mfa_factors where user_id = (select id from auth.users where email = '${ADMIN}');`);
  await signIn(page, ADMIN);
  await expect(page.getByRole("heading", { name: "Sign-in code" })).toBeVisible({ timeout: 30_000 });
  await page.getByText("Or enter this key by hand").click();
  const secret = (await page.locator("details code").textContent())?.trim();
  if (!secret) throw new Error("The MFA page showed no key");
  await page.getByLabel("6-digit code").fill(totp(secret));
  await page.getByRole("button", { name: "Verify" }).click();
}

/** The first link in the newest email to `to`, waiting for it to arrive. */
export async function emailLink(to: string): Promise<string> {
  let id: string | undefined;
  await expect
    .poll(
      async () => {
        const res = await fetch(`${MAILPIT}/api/v1/search?query=${encodeURIComponent(`to:${to}`)}`);
        id = ((await res.json()) as { messages: { ID: string }[] }).messages[0]?.ID;
        return id;
      },
      { timeout: 15_000, message: `email to ${to}` },
    )
    .toBeTruthy();
  const msg = (await (await fetch(`${MAILPIT}/api/v1/message/${id}`)).json()) as { HTML: string };
  const href = msg.HTML.match(/href="([^"]+)"/)?.[1];
  if (!href) throw new Error(`No link in the email to ${to}`);
  return href.replace(/&amp;/g, "&");
}

/** WCAG 2.2 AA via axe; fails with the rule ids and the offending selectors. */
export async function expectAccessible(page: Page) {
  const { violations } = await new AxeBuilder({ page }).withTags(["wcag2a", "wcag2aa", "wcag21a", "wcag21aa", "wcag22aa"]).analyze();
  expect(violations.map((v) => `${v.id}: ${v.nodes.map((n) => n.target.join(" ")).join(", ")}`)).toEqual([]);
}
