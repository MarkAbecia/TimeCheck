import { expect, test } from "@playwright/test";
import { LEADER_STATE, MEMBER, expectAccessible, signIn } from "../helpers";

test.use({ storageState: LEADER_STATE });

// Titles start "E2E " so teardown can remove the events and their marks.
const today = () => new Intl.DateTimeFormat("en-CA", { timeZone: "Asia/Manila" }).format(new Date());

test("a leader schedules a cell meeting, marks the roster in three taps, and the member sees it in their history", async ({
  page,
  browser,
}) => {
  const title = `E2E Cell ${Date.now().toString(36)}`;

  await page.goto("/events");
  await page.getByRole("link", { name: "Schedule" }).click();
  await expect(page.getByRole("heading", { name: "Schedule an event" })).toBeVisible();
  await expectAccessible(page);

  await page.getByLabel("Title", { exact: true }).fill(title);
  await page.getByLabel("Date", { exact: true }).fill(today());
  // Early this morning, so it has already started and can be marked.
  await page.getByLabel("Starts", { exact: true }).fill("00:05");
  await page.getByLabel("Ends", { exact: true }).fill("00:35");
  await page.getByRole("button", { name: "Schedule", exact: true }).click();
  // An earlier run may have left a clash on the same day; the warning step is the flow under test either way.
  const anyway = page.getByRole("button", { name: "Schedule anyway" });
  const heading = page.getByRole("heading", { name: title });
  await expect(anyway.or(heading)).toBeVisible();
  if (await anyway.isVisible()) await anyway.click();
  await expect(heading).toBeVisible();

  // Mark all present, tap the exception, save.
  await page.getByRole("button", { name: "Mark all present" }).click();
  // The radio is visually hidden inside its label; tap the label, as a person would.
  await page.getByRole("radio", { name: "Absent" }).first().locator("..").click();
  await expectAccessible(page); // before the toast, which axe catches mid-fade
  await page.getByRole("button", { name: /^Save \d+ changes$/ }).click();
  await expect(page.getByText(/^Saved \d+ marks$/)).toBeVisible();
  await expect(page.getByRole("button", { name: "All saved" })).toBeDisabled();

  // The marks survive a reload.
  await page.reload();
  await expect(page.getByRole("radio", { name: "Absent" }).first()).toBeChecked();
  await expect(page.getByRole("button", { name: "Mark all present" })).toHaveCount(0);

  // The member, in their own session, finds the meeting in their history.
  const ctx = await browser.newContext({ storageState: { cookies: [], origins: [] } });
  const member = await ctx.newPage();
  await signIn(member, MEMBER);
  await member.waitForURL("/");
  await member.goto("/history");
  await expect(member.getByRole("heading", { name: "My attendance" })).toBeVisible();
  await expect(member.getByRole("link", { name: new RegExp(title) })).toBeVisible();
  await expectAccessible(member);
  await ctx.close();
});
