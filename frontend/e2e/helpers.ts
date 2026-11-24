import AxeBuilder from "@axe-core/playwright";
import { expect, type Page } from "@playwright/test";

// Seeded dev accounts (database/supabase/seed.sql). Local only.
export const PASSWORD = "TimeCheck-dev-123!";
export const LEADER = "leader@timecheck.test";
export const MEMBER = "member@timecheck.test";
export const LEADER_STATE = "e2e/.auth/leader.json";

/** People the specs create use this domain, so teardown can find and remove exactly them. */
export const E2E_DOMAIN = "e2e.timecheck.test";
export const uniqueEmail = (name: string) => `${name}.${Date.now().toString(36)}${Math.random().toString(36).slice(2, 6)}@${E2E_DOMAIN}`;

const MAILPIT = process.env.MAILPIT_URL ?? "http://127.0.0.1:55424";

export async function signIn(page: Page, email: string, password = PASSWORD) {
  await page.goto("/login");
  await page.getByLabel("Email", { exact: true }).fill(email);
  await page.getByLabel("Password", { exact: true }).fill(password);
  await page.getByRole("button", { name: "Sign in" }).click();
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
