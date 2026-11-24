import { expect, test } from "@playwright/test";
import { LEADER_STATE, MEMBER, expectAccessible, signIn } from "../helpers";

// Texts start "E2E " so teardown can remove them. A 1×1 PNG stands in for a notebook photo.
const PNG = Buffer.from("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8DwHwAFBQIAX8jx0gAAAABJRU5ErkJggg==", "base64");

test("a member sends a devotion with a photo in a few taps, and their leader reviews it", async ({ browser }) => {
  const text = `E2E Psalm 23 ${Date.now().toString(36)}`;

  const memberCtx = await browser.newContext({ storageState: { cookies: [], origins: [] } });
  const member = await memberCtx.newPage();
  await signIn(member, MEMBER);
  await member.waitForURL("/");
  await member.getByRole("link", { name: "Devotions" }).first().click();
  await expect(member.getByRole("heading", { name: "Devotions", level: 1 })).toBeVisible();
  await expectAccessible(member);

  const day = member.getByRole("region", { name: "Day devotion" });
  await day.getByLabel("What did you read and reflect on?").fill(text);
  // A run that failed half way may have left a photo; start from the picker either way.
  const leftover = day.getByRole("button", { name: "Remove" });
  if (await leftover.isVisible()) await leftover.click();
  await day.locator('input[type="file"]:not([capture])').setInputFiles({ name: "notebook.png", mimeType: "image/png", buffer: PNG });
  await expect(day.getByText("Photo ready to send.")).toBeVisible();
  await day.getByRole("button", { name: /^(Send devotion|Save changes)$/ }).click();
  await expect(member.getByText(/^Devotion (sent\. Well done!|updated\.)$/)).toBeVisible();
  await expect(day.getByText("Your photo is saved with this devotion.")).toBeVisible();

  // The leader finds it in the group view, opens it, and marks it reviewed with a private note.
  const leaderCtx = await browser.newContext({ storageState: LEADER_STATE });
  const leader = await leaderCtx.newPage();
  await leader.goto("/devotions/group");
  await expect(leader.getByRole("heading", { name: "Group devotions" })).toBeVisible();
  await expect(leader.getByText(/AI scores are estimates/)).toBeVisible();
  await expectAccessible(leader);
  const row = leader.getByRole("listitem").filter({ hasText: "Nico Bautista" });
  await row.getByRole("link").first().click();
  await expect(leader.getByText(text)).toBeVisible();
  await expect(leader.getByRole("img", { name: /Devotion photo from/ })).toBeVisible();
  await leader.getByLabel("Private note (optional)").fill("Talked after cell.");
  await leader.getByRole("button", { name: /^(Mark reviewed|Update review)$/ }).click();
  await expect(leader.getByText(/^Reviewed /)).toBeVisible();
  await expectAccessible(leader);
  await leaderCtx.close();

  // Tidy up through the app, so the photo is deleted from Storage properly.
  await member.reload();
  await day.getByRole("button", { name: "Remove" }).click();
  await day.getByRole("button", { name: "Save changes" }).click();
  await expect(member.getByText("Devotion updated.")).toBeVisible();
  await memberCtx.close();
});
