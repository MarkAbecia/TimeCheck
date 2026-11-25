import { readFile } from "node:fs/promises";
import { expect, test } from "@playwright/test";
import { LEADER, LEADER_STATE, expectAccessible, sql } from "../helpers";

test.use({ storageState: LEADER_STATE });

test("a leader filters a report, exports it as CSV and finds it under their exports", async ({ page }) => {
  await page.goto("/reports");
  await page.getByRole("link", { name: /^Attendance/ }).click();
  await expect(page.getByRole("heading", { name: "Attendance", level: 1 })).toBeVisible();
  await expect(page.getByText("Group: Joshua Cell")).toBeVisible();

  // On a phone the filters are folded away under the report's title.
  await page.getByText("Change filters").click();
  await page.getByLabel("Events").selectOption({ label: "Weekly" });
  await page.getByRole("button", { name: "Show report" }).click();
  await expect(page).toHaveURL(/events=category%3AWEEKLY/);

  const download = page.waitForEvent("download");
  await page.getByRole("button", { name: "CSV" }).click();
  const file = await download;
  expect(file.suggestedFilename()).toMatch(/^timecheck-attendance-\d{4}-\d{2}-\d{2}\.csv$/);
  expect(await readFile(await file.path(), "utf8")).toContain("Attendance");

  await page.goto("/reports/exports");
  await expect(page.getByRole("heading", { name: "Your exports", level: 1 })).toBeVisible();
  await expect(page.getByRole("button", { name: "Download" }).first()).toBeVisible();
  await expectAccessible(page);

  // Due now, so a running Worker deletes the file and marks it expired within a minute.
  sql(`update public.report_jobs set expires_at = now() - interval '1 minute'
       where status = 'DONE' and requested_by = (select id from public.app_users where email = '${LEADER}');`);
});
