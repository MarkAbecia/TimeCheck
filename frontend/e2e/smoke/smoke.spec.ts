import { expect, test } from "@playwright/test";
import { signIn, totp } from "../helpers";

// Accounts made once per environment for smoke testing (runbooks/deploy-and-rollback.md). The admin has a TOTP factor whose
// key is stored only as a CI secret.
const leader = { email: process.env.SMOKE_LEADER_EMAIL ?? "", password: process.env.SMOKE_LEADER_PASSWORD ?? "" };
const admin = {
  email: process.env.SMOKE_ADMIN_EMAIL ?? "",
  password: process.env.SMOKE_ADMIN_PASSWORD ?? "",
  totpSecret: process.env.SMOKE_ADMIN_TOTP_SECRET ?? "",
};

test("the web service is up and sends its security headers", async ({ request }) => {
  const health = await request.get("/api/health");
  expect(health.ok()).toBe(true);
  expect((await health.json()).status).toBe("ok");

  const login = await request.get("/login");
  expect(login.ok()).toBe(true);
  const headers = login.headers();
  expect(headers["content-security-policy"]).toContain("default-src 'self'");
  expect(headers["x-content-type-options"]).toBe("nosniff");
  if (new URL(login.url()).protocol === "https:") expect(headers["strict-transport-security"]).toContain("max-age=");
});

test("signed-out visitors are sent to sign in, and the API refuses them", async ({ page, request }) => {
  await page.goto("/members");
  await expect(page).toHaveURL(/\/login/);
  expect((await request.get("/api/bff/me")).status()).toBe(401);
});

test("a leader signs in, sees Today and opens a report", async ({ page }) => {
  test.skip(!leader.email || !leader.password, "SMOKE_LEADER_EMAIL and SMOKE_LEADER_PASSWORD aren't set");
  await signIn(page, leader.email, leader.password);
  await expect(page.getByRole("heading", { level: 1 })).toContainText("Today", { timeout: 20_000 });

  await page.goto("/reports");
  await page.getByRole("link", { name: /^Attendance/ }).click();
  await expect(page.getByRole("heading", { name: "Attendance", level: 1 })).toBeVisible();
  await expect(page.getByText(/^Updated /)).toBeVisible();

  await page.getByRole("button", { name: "Sign out" }).click();
  await expect(page).toHaveURL(/\/login/);
});

test("an admin signs in with a code and sees members", async ({ page }) => {
  test.skip(!admin.email || !admin.password || !admin.totpSecret, "SMOKE_ADMIN_EMAIL, _PASSWORD and _TOTP_SECRET aren't set");
  await signIn(page, admin.email, admin.password);
  await expect(page.getByRole("heading", { name: "Sign-in code" })).toBeVisible({ timeout: 20_000 });
  await page.getByLabel("6-digit code").fill(totp(admin.totpSecret));
  await page.getByRole("button", { name: "Verify" }).click();
  await expect(page.getByRole("heading", { level: 1 })).toContainText("Today", { timeout: 20_000 });

  await page.goto("/members");
  await expect(page.getByRole("heading", { level: 1 })).toBeVisible();
  await page.getByRole("button", { name: "Sign out" }).click();
  await expect(page).toHaveURL(/\/login/);
});
