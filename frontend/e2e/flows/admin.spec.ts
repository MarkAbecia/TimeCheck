import { expect, test } from "@playwright/test";
import { ADMIN, expectAccessible, forgetCachedUser, signIn, signInAsAdmin, sql, totp, uniqueEmail } from "../helpers";

test("the TOTP helper matches RFC 6238", () => {
  // Appendix B, SHA-1, T = 59 s: 94287082, of which an authenticator shows the last 6 digits.
  expect(totp("GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ", 59_000)).toBe("287082");
});

test("an admin signs in with a code, finds a member, resets their code, then deactivates, exports and erases them", async ({ page }) => {
  test.setTimeout(90_000); // the longest flow: sign-in with a code, register, search, reset, export, deactivate, erase
  await signInAsAdmin(page);
  await expect(page.getByRole("heading", { level: 1 })).toContainText("Today", { timeout: 30_000 });

  // Someone to erase, registered here so nothing seeded is touched.
  const group = sql(`select id from public.cell_groups where name = 'Joshua Cell' limit 1;`);
  const last = `Erase${Date.now().toString(36)}`;
  await page.goto(`/members/new?group=${group}`);
  await page.getByLabel("First name").fill("Ivy");
  await page.getByLabel("Last name").fill(last);
  await page.getByLabel("Gender").selectOption("FEMALE");
  await page.getByLabel("Birthdate").fill("1995-02-03");
  await page.getByLabel("Email", { exact: true }).fill(uniqueEmail("ivy"));
  await page
    .getByLabel("Mobile number", { exact: true })
    .fill(`0917 ${Math.floor(100 + Math.random() * 900)} ${Math.floor(1000 + Math.random() * 9000)}`);
  await page.getByRole("button", { name: "Register" }).click();
  await expect(page.getByRole("heading", { name: `Ivy ${last}` })).toBeVisible();
  const id = page.url().match(/members\/([0-9a-f-]{36})/)?.[1];
  expect(id).toBeTruthy();

  // Search finds her from the Members page by part of her surname, in any case.
  await page.goto("/members");
  await page.getByRole("searchbox", { name: "Search by name, email or mobile" }).fill(last.toLowerCase());
  await page.getByRole("button", { name: "Search" }).click();
  await expect(page.getByText(/^1 person matches/)).toBeVisible();
  await expectAccessible(page);
  await page.getByRole("link", { name: new RegExp(`Ivy ${last}`) }).click();
  await expect(page.getByRole("heading", { name: `Ivy ${last}` })).toBeVisible();

  const account = page.getByRole("region", { name: "Account and data" });
  await expect(account.getByText("Active. They can sign in.")).toBeVisible();
  await expectAccessible(page);

  // She lost the phone with her code: the reset goes through Supabase's admin API and removes the factor.
  const factors = `select count(*) from auth.mfa_factors f join public.app_users u on u.auth_user_id = f.user_id where u.id = '${id}'`;
  sql(`insert into auth.mfa_factors (id, user_id, friendly_name, factor_type, status, created_at, updated_at, secret)
       select gen_random_uuid(), auth_user_id, 'e2e-lost-phone', 'totp', 'verified', now(), now(), 'JBSWY3DPEHPK3PXP'
       from public.app_users where id = '${id}';`);
  expect(sql(`${factors};`)).toBe("1");
  await account.getByRole("button", { name: "Reset sign-in code" }).click();
  await account.getByRole("button", { name: "Yes, reset" }).click();
  await expect(account.getByText("Done. They'll set up a new code the next time they sign in.")).toBeVisible({ timeout: 15_000 }); // a Supabase admin call; slow in dev
  expect(sql(`${factors};`)).toBe("0");

  // Their data, as one JSON file.
  const download = page.waitForEvent("download");
  await account.getByRole("button", { name: "Download their data" }).click();
  const file = await download;
  expect(file.suggestedFilename()).toBe(`timecheck-data-${id}.json`);
  const data = JSON.parse(await (await import("node:fs/promises")).readFile((await file.path())!, "utf8"));
  expect(JSON.stringify(data)).toContain(last);

  // Erasing is offered only after deactivation, which asks first.
  await expect(account.getByRole("button", { name: "Erase for good" })).toHaveCount(0);
  await account.getByRole("button", { name: "Deactivate" }).click();
  await account.getByRole("button", { name: "Yes, deactivate" }).click();
  await expect(account.getByText("Deactivated. They can't sign in.")).toBeVisible();

  const erase = account.getByRole("button", { name: "Erase for good" });
  await expect(erase).toBeDisabled();
  await account.getByLabel(`Type Ivy ${last} to confirm`).fill(`Ivy ${last}`);
  await erase.click();
  await expect(page).toHaveURL(/\/members\?group=/);
  expect(
    sql(`select deleted_at is not null and auth_user_id is null and email like 'erased-%' from public.app_users where id = '${id}';`),
  ).toBe("t");
});

test("an admin signs a member out on every device, and they can sign in again", async ({ page, browser }) => {
  test.setTimeout(90_000);
  // Lia (seeded member2) signs in on her own device.
  const lia = await browser.newContext();
  const liaPage = await lia.newPage();
  await signIn(liaPage, "member2@timecheck.test");
  await expect(liaPage.getByRole("heading", { level: 1 })).toContainText("Today", { timeout: 30_000 });

  try {
    await signInAsAdmin(page);
    await expect(page.getByRole("heading", { level: 1 })).toContainText("Today", { timeout: 30_000 });
    await page.goto("/members/10000000-0000-0000-0000-000000000006");
    const account = page.getByRole("region", { name: "Account and data" });
    await account.getByRole("button", { name: "Sign out everywhere" }).click();
    await account.getByRole("button", { name: "Yes, sign out" }).click();
    await expect(account.getByText("Done. They're signed out everywhere within a minute.")).toBeVisible({ timeout: 15_000 }); // a Supabase admin call; slow in dev
    expect(sql(`select count(*) from auth.sessions where user_id = '30000000-0000-0000-0000-000000000006';`)).toBe("0");

    // Her next page load lands on sign-in (her token hasn't expired, but the API refuses it), and signing in works again.
    await liaPage.goto("/");
    await expect(liaPage).toHaveURL(/\/login/);
    await expect(liaPage.getByRole("heading", { name: "Sign in" })).toBeVisible();
    await signIn(liaPage, "member2@timecheck.test");
    await expect(liaPage.getByRole("heading", { level: 1 })).toContainText("Today", { timeout: 30_000 });
  } finally {
    await lia.close();
    sql(`update public.app_users set sessions_revoked_at = null where id = '10000000-0000-0000-0000-000000000006';`); // seed state
  }
});

test("a super admin reads one person's history in the audit log", async ({ page }) => {
  test.setTimeout(90_000);
  // The seeded super admin may carry a real authenticator, so the test admin is promoted for this test instead.
  const ADMIN_AUTH = "30000000-0000-0000-0000-000000000002";
  const LIA = "10000000-0000-0000-0000-000000000006";
  const setRole = (role: string) => {
    sql(`update public.app_users set role = '${role}' where email = '${ADMIN}';`);
    forgetCachedUser(ADMIN_AUTH);
  };
  setRole("SUPER_ADMIN");
  try {
    await signInAsAdmin(page);
    await expect(page.getByRole("heading", { level: 1 })).toContainText("Today", { timeout: 30_000 });

    // Something to find: signing Lia out everywhere is audited.
    await page.goto(`/members/${LIA}`);
    const account = page.getByRole("region", { name: "Account and data" });
    await account.getByRole("button", { name: "Sign out everywhere" }).click();
    await account.getByRole("button", { name: "Yes, sign out" }).click();
    await expect(account.getByText(/^Done\. They're signed out/)).toBeVisible({ timeout: 15_000 }); // a Supabase admin call; slow in dev

    await page.goto("/audit");
    await expect(page.getByRole("heading", { name: "Audit log" })).toBeVisible();
    await expectAccessible(page);

    // Filter by action, then narrow to Lia by following her name.
    await page.getByText("Filters", { exact: true }).click();
    await page.getByLabel("Action").selectOption("member.force_logout");
    await page.getByRole("button", { name: "Show" }).click();
    const first = page.getByRole("listitem").filter({ hasText: "member.force_logout" }).first();
    await expect(first).toContainText("By Paolo Reyes");
    await first.getByRole("link", { name: "Lia Ramos" }).click();
    await expect(page.getByRole("link", { name: /About Lia Ramos/ })).toBeVisible();
    await expect(page).toHaveURL(new RegExp(`entityId=${LIA}`));
    await expect(page).toHaveURL(/action=member\.force_logout/);

    // Removing the person filter keeps the action filter.
    await page.getByRole("link", { name: /About Lia Ramos/ }).click();
    await expect(page).not.toHaveURL(/entityId=/);
    await expect(page.getByLabel("Action")).toHaveValue("member.force_logout");
  } finally {
    setRole("ADMIN");
    sql(`update public.app_users set sessions_revoked_at = null where id = '${LIA}';`); // seed state
  }
});
