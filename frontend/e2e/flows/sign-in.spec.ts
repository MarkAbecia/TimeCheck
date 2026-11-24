import { expect, test } from "@playwright/test";
import { MEMBER, signIn } from "../helpers";

test("a wrong password says so and keeps you on sign-in", async ({ page }) => {
  await signIn(page, MEMBER, "not-the-password-1");
  await expect(page.getByRole("alert").filter({ hasText: /./ })).toContainText("don't match");
  await expect(page).toHaveURL(/\/login/);
});

test("a member signs in to Today, can't open Members, and signs out", async ({ page }) => {
  await signIn(page, MEMBER);
  await expect(page.getByRole("heading", { level: 1 })).toContainText("Today");

  await page.goto("/members");
  await expect(page.getByRole("heading", { name: "Page not found" })).toBeVisible();

  await page.goto("/profile");
  await expect(page.getByLabel("Nickname")).toBeVisible();
  // The profile asks for the current password before changing it.
  await page.getByRole("button", { name: "Change password" }).click();
  await expect(page.getByText("Enter your password.").first()).toBeVisible();

  await page.goto("/");
  await page
    .getByRole("button", { name: /sign out/i })
    .filter({ visible: true })
    .click();
  await expect(page).toHaveURL(/\/login/);
  await page.goto("/");
  await expect(page).toHaveURL(/\/login/);
});
