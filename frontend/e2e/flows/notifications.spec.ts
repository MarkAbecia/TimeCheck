import { expect, test } from "@playwright/test";
import { MEMBER, expectAccessible, signIn, sql } from "../helpers";

// An "event changed" notice about a real upcoming church-wide event, titled "E2E " so teardown removes it.
function queueForMember(title: string) {
  sql(`insert into public.notifications (church_id, user_id, type, payload)
       select u.church_id, u.id, 'event.changed', jsonb_build_object('eventId', e.id, 'title', '${title}', 'startsAt', e.starts_at)
       from public.app_users u, lateral (select id, starts_at from public.events where group_id is null and cancelled_at is null and starts_at > now()
                                         order by starts_at limit 1) e
       where u.email = '${MEMBER}';`);
}

test("a member sees a new notification, opens it, and chooses how reminders reach them", async ({ browser }) => {
  const title = `E2E Cell Celebration ${Date.now().toString(36)}`;
  queueForMember(title);

  const ctx = await browser.newContext({ storageState: { cookies: [], origins: [] } });
  const page = await ctx.newPage();
  await signIn(page, MEMBER);
  await page.waitForURL("/");
  await page.goto("/notifications");
  await expect(page.getByRole("heading", { name: "Notifications", level: 1 })).toBeVisible();
  await expect(page.getByText(/\d+ unread/).first()).toBeAttached(); // the badge, in words for screen readers
  const item = page.getByRole("link", { name: new RegExp(`New Changed: ${title}`) });
  await expect(item).toBeVisible();
  await expectAccessible(page);

  await item.click();
  await page.waitForURL(/\/events\//);
  await page.goto("/notifications");
  await expect(page.getByRole("link", { name: new RegExp(`^Changed: ${title}`) })).toBeVisible();

  // Devotion reminders by email too, then back to the default.
  await page.goto("/profile#notifications");
  const devotions = page.getByRole("group", { name: "Devotion reminders" });
  await devotions.getByLabel("Email").check();
  await page.getByRole("button", { name: "Save choices" }).click();
  await expect(page.getByText("Notification choices saved.")).toBeVisible();
  await page.reload();
  await expect(devotions.getByLabel("Email")).toBeChecked();
  await expect(page.getByRole("group", { name: "Attendance to mark" })).toHaveCount(0);
  await expectAccessible(page);
  await devotions.getByLabel("Email").uncheck();
  await page.getByRole("button", { name: "Save choices" }).click();
  await expect(page.getByText("Notification choices saved.")).toBeVisible();
  await ctx.close();
});
