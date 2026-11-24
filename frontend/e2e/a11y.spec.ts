import { expect, test } from "@playwright/test";
import { LEADER_STATE, expectAccessible } from "./helpers";

// axe on every screen built so far, at each project's viewport and colour scheme (§11.3, §13).
test("sign-in and forgot password", async ({ page }) => {
  for (const path of ["/login", "/forgot-password"]) {
    await page.goto(path);
    await expect(page.getByRole("heading", { level: 1 })).toBeVisible();
    await expectAccessible(page);
  }
});

test.describe("signed in as a leader", () => {
  test.use({ storageState: LEADER_STATE });

  for (const path of [
    "/",
    "/members",
    "/members/new",
    "/profile",
    "/events",
    "/events/new",
    "/history",
    "/devotions",
    "/devotions/group",
    "/invites",
    "/notifications",
  ]) {
    test(path, async ({ page }) => {
      await page.goto(path);
      await expect(page.getByRole("heading", { level: 1 })).toBeVisible();
      await expectAccessible(page);
    });
  }
});
