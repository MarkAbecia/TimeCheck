import { expect, test, type Page } from "@playwright/test";
import { LEADER_STATE, emailLink, expectAccessible, uniqueEmail } from "../helpers";

test.use({ storageState: LEADER_STATE });

const mobile = () => `0917 ${Math.floor(100 + Math.random() * 900)} ${Math.floor(1000 + Math.random() * 9000)}`;

async function fillAdult(page: Page, p: { first: string; last: string; email: string; mobile: string; birthdate?: string }) {
  await page.getByLabel("First name").fill(p.first);
  await page.getByLabel("Last name").fill(p.last);
  await page.getByLabel("Gender").selectOption("FEMALE");
  await page.getByLabel("Birthdate").fill(p.birthdate ?? "1996-04-10");
  await page.getByLabel("Email", { exact: true }).fill(p.email);
  await page.getByLabel("Mobile number", { exact: true }).fill(p.mobile);
}

test("a leader registers a member, who accepts the invite and finishes onboarding", async ({ page, browser }) => {
  const email = uniqueEmail("bea");
  const last = `Lopez${Date.now().toString(36)}`;

  await page.goto("/members");
  await page.getByRole("link", { name: "Register a member" }).click();
  await expect(page.getByRole("heading", { name: "Register a member" })).toBeVisible();
  await expectAccessible(page);

  // Validation runs before anything is sent.
  await page.getByRole("button", { name: "Register" }).click();
  await expect(page.getByText("Fill this in.").first()).toBeVisible();

  await fillAdult(page, { first: "Bea", last, email, mobile: mobile() });
  await page.getByRole("button", { name: "Register" }).click();
  await expect(page.getByRole("heading", { name: `Bea ${last}` })).toBeVisible();
  await expect(page.getByText(email)).toBeVisible();

  // The new member, in a fresh browser with no session, follows the emailed link.
  const link = await emailLink(email);
  const ctx = await browser.newContext();
  const invitee = await ctx.newPage();
  await invitee.goto(link);
  await expect(invitee.getByRole("heading", { name: "Choose your password" })).toBeVisible();
  await invitee.getByLabel("New password", { exact: true }).fill("Bea-chooses-4-herself");
  await invitee.getByLabel("Confirm new password").fill("Bea-chooses-4-herself");
  await invitee.getByRole("button", { name: "Save password" }).click();

  await expect(invitee.getByRole("heading", { name: "Your privacy" })).toBeVisible();
  await invitee.getByRole("button", { name: "I understand and agree" }).click();
  await expect(invitee.getByRole("heading", { level: 1 })).toContainText("Today");

  // The link is single use.
  const again = await (await browser.newContext()).newPage();
  await again.goto(link);
  await expect(again).toHaveURL(/forgot-password\?expired=1/);
  await ctx.close();
});

test("a minor needs a guardian who agreed", async ({ page }) => {
  await page.goto("/members/new");
  await fillAdult(page, {
    first: "Kiko",
    last: `Ramos${Date.now().toString(36)}`,
    email: uniqueEmail("kiko"),
    mobile: mobile(),
    birthdate: `${new Date().getFullYear() - 13}-06-01`,
  });
  await expect(page.getByRole("group", { name: "Parent or guardian" })).toBeVisible();
  await expectAccessible(page);

  await page.getByRole("button", { name: "Register" }).click();
  await expect(page.getByText("A parent or guardian has to agree first.")).toBeVisible();

  await page.getByLabel("Guardian's name").fill("Nena Ramos");
  await page.getByLabel("Guardian's mobile or email").fill("0918 222 3333");
  await page.getByLabel(/The guardian agreed/).check();
  await page.getByRole("button", { name: "Register" }).click();
  await expect(page.getByRole("heading", { name: /^Kiko / })).toBeVisible();
});

test("a possible duplicate asks before registering", async ({ page }) => {
  const shared = mobile();
  await page.goto("/members/new");
  await fillAdult(page, { first: "Cora", last: `Diaz${Date.now().toString(36)}`, email: uniqueEmail("cora"), mobile: shared });
  await page.getByRole("button", { name: "Register" }).click();
  await expect(page.getByRole("heading", { name: /^Cora / })).toBeVisible();

  // Same number written the international way, different person.
  await page.goto("/members/new");
  await fillAdult(page, {
    first: "Dina",
    last: `Cruz${Date.now().toString(36)}`,
    email: uniqueEmail("dina"),
    mobile: `+63 ${shared.slice(1)}`,
  });
  await page.getByRole("button", { name: "Register" }).click();
  await expect(page.getByText("This might be someone already registered")).toBeVisible();
  await expect(page.getByText("The mobile number matches a member in this group.")).toBeVisible();
  await expectAccessible(page);

  await page.getByRole("button", { name: "Register anyway" }).click();
  await expect(page.getByRole("heading", { name: /^Dina / })).toBeVisible();
});

test("a leader changes a member's status", async ({ page }) => {
  await page.goto("/members");
  await page
    .getByRole("link")
    .filter({ hasText: /^Cora / })
    .first()
    .click();
  await page.getByLabel("Status", { exact: true }).selectOption("DISCIPLE");
  await page.getByLabel("Reason (optional)").first().fill("Finished the foundations class");
  await page.getByRole("button", { name: "Save" }).first().click();
  await expect(page.getByText(/^Saved /)).toBeVisible();
  await page.reload();
  await expect(page.getByLabel("Status", { exact: true })).toHaveValue("DISCIPLE");
});
