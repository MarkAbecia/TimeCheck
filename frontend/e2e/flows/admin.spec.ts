import { expect, test } from "@playwright/test";
import { expectAccessible, signInAsAdmin, sql, totp, uniqueEmail } from "../helpers";

test("the TOTP helper matches RFC 6238", () => {
  // Appendix B, SHA-1, T = 59 s: 94287082, of which an authenticator shows the last 6 digits.
  expect(totp("GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ", 59_000)).toBe("287082");
});

test("an admin signs in with a code, then deactivates, exports and erases a member", async ({ page }) => {
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

  const account = page.getByRole("region", { name: "Account and data" });
  await expect(account.getByText("Active. They can sign in.")).toBeVisible();
  await expectAccessible(page);

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
