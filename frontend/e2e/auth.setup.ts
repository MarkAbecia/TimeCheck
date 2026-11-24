import { expect, test as setup } from "@playwright/test";
import { LEADER, LEADER_STATE, signIn } from "./helpers";

// One leader sign-in shared by every spec, so the login rate limit (5/min per account) is never hit.
setup("sign in as the leader", async ({ page }) => {
  await signIn(page, LEADER);
  await expect(page.getByRole("heading", { level: 1 })).toContainText("Today");
  await page.context().storageState({ path: LEADER_STATE });
});
