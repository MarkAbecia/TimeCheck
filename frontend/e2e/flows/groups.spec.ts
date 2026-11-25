import { expect, test, type Page } from "@playwright/test";
import { expectAccessible, signInAsAdmin, sql, uniqueEmail } from "../helpers";

// Admin-only group and member management (§6.10): create, rename, assign a leader, archive; change a role; move a member.
// Everything here is new ("E2E " group, @e2e person), so nothing seeded is touched; the teardown removes both.

async function register(page: Page, groupId: string, last: string) {
  await page.goto(`/members/new?group=${groupId}`);
  await page.getByLabel("First name").fill("Gabe");
  await page.getByLabel("Last name").fill(last);
  await page.getByLabel("Gender").selectOption("MALE");
  await page.getByLabel("Birthdate").fill("1992-06-07");
  await page.getByLabel("Email", { exact: true }).fill(uniqueEmail("gabe"));
  await page
    .getByLabel("Mobile number", { exact: true })
    .fill(`0918 ${Math.floor(100 + Math.random() * 900)} ${Math.floor(1000 + Math.random() * 9000)}`);
  await page.getByRole("button", { name: "Register" }).click();
  await expect(page.getByRole("heading", { name: `Gabe ${last}` })).toBeVisible();
  return page.url().match(/members\/([0-9a-f-]{36})/)![1];
}

test("an admin creates a group, moves a member into it, makes them its leader, and archives it once it's empty", async ({ page }) => {
  test.setTimeout(120_000); // sign-in with a code, register, then a dozen saves
  await signInAsAdmin(page);
  await expect(page.getByRole("heading", { level: 1 })).toContainText("Today", { timeout: 30_000 });

  const suffix = Date.now().toString(36);
  const name = `E2E Cell ${suffix}`;
  const joshua = sql(`select id from public.cell_groups where name = 'Joshua Cell' limit 1;`);

  // A new group, with no leader yet.
  await page.goto("/groups");
  const newGroup = page.getByRole("region", { name: "New group" });
  await newGroup.getByLabel("Group name").fill(name);
  await newGroup.getByRole("button", { name: "Create" }).click();
  await expect(page.getByText(`Created ${name}.`)).toBeVisible({ timeout: 15_000 });
  const row = page.getByRole("listitem").filter({ has: page.getByText(name, { exact: true }) });
  await expect(row).toContainText("No leader yet, 0 members");
  await expectAccessible(page);
  const groupId = sql(`select id from public.cell_groups where name = '${name}';`);

  // Someone to move, registered into Joshua Cell, then moved to the new group.
  const last = `Move${suffix}`;
  const id = await register(page, joshua, last);
  const groupForm = page.locator("form").filter({ has: page.getByLabel("Group", { exact: true }) });
  await groupForm.getByLabel("Group", { exact: true }).selectOption({ label: name });
  await groupForm.getByRole("button", { name: "Save" }).click();
  await expect(page.getByText(`Saved Gabe ${last}.`)).toBeVisible({ timeout: 15_000 });
  expect(sql(`select group_id from public.group_memberships where user_id = '${id}' and left_on is null;`)).toBe(groupId);

  // Promoted to leader, with a reason; the change is recorded.
  const roleForm = page.locator("form").filter({ has: page.getByLabel("Role", { exact: true }) });
  await roleForm.getByLabel("Role", { exact: true }).selectOption({ label: "Leader" });
  await roleForm.getByLabel("Reason (optional)").fill("Leads the new cell");
  await roleForm.getByRole("button", { name: "Save" }).click();
  await expect(roleForm.getByRole("button", { name: "Save" })).toBeDisabled({ timeout: 15_000 }); // saved: the select matches again
  expect(sql(`select role from public.app_users where id = '${id}';`)).toBe("LEADER");
  expect(sql(`select to_role || '|' || reason from public.role_changes where user_id = '${id}' order by changed_at desc limit 1;`)).toBe(
    "LEADER|Leads the new cell",
  );

  // Rename the group and make them its leader.
  await page.goto("/groups");
  await row.getByRole("button", { name: "Edit" }).click();
  const renamed = `${name} North`;
  const editForm = page.locator("form").filter({ has: page.getByRole("button", { name: "Cancel" }) });
  await editForm.getByLabel("Group name").fill(renamed);
  await editForm.getByLabel("Leader").selectOption({ label: `Gabe ${last}` });
  await editForm.getByRole("button", { name: "Save" }).click();
  await expect(page.getByText(`Saved ${renamed}.`)).toBeVisible({ timeout: 15_000 });
  const renamedRow = page.getByRole("listitem").filter({ has: page.getByText(renamed, { exact: true }) });
  await expect(renamedRow).toContainText(`Gabe ${last}, 1 member`);

  // A group with members can't be archived.
  await renamedRow.getByRole("button", { name: "Archive" }).click();
  await renamedRow.getByRole("button", { name: "Yes, archive" }).click();
  await expect(renamedRow.getByText("Move this group's members to another group first.")).toBeVisible({ timeout: 15_000 });

  // Nor can a leader be made a member while they still lead a group.
  await page.goto(`/members/${id}`);
  await roleForm.getByLabel("Role", { exact: true }).selectOption({ label: "Member" });
  await roleForm.getByRole("button", { name: "Save" }).click();
  await expect(roleForm.getByText(`Gabe ${last} still leads ${renamed}. Assign another leader there first.`)).toBeVisible({
    timeout: 15_000,
  });

  // Empty it: no leader, and the member moves back to Joshua Cell. Then it archives and leaves the list.
  await page.goto("/groups");
  await renamedRow.getByRole("button", { name: "Edit" }).click();
  await editForm.getByLabel("Leader").selectOption({ label: "No leader yet" });
  await editForm.getByRole("button", { name: "Save" }).click();
  await expect(renamedRow).toContainText("No leader yet, 1 member", { timeout: 15_000 });
  await page.goto(`/members/${id}`);
  await groupForm.getByLabel("Group", { exact: true }).selectOption({ label: "Joshua Cell" });
  await groupForm.getByRole("button", { name: "Save" }).click();
  await expect(groupForm.getByRole("button", { name: "Save" })).toBeDisabled({ timeout: 15_000 });
  expect(sql(`select group_id from public.group_memberships where user_id = '${id}' and left_on is null;`)).toBe(joshua);

  await page.goto("/groups");
  await renamedRow.getByRole("button", { name: "Archive" }).click();
  await renamedRow.getByRole("button", { name: "Yes, archive" }).click();
  await expect(renamedRow).toHaveCount(0, { timeout: 15_000 });
  expect(sql(`select archived_at is not null from public.cell_groups where id = '${groupId}';`)).toBe("t");
  expect(
    sql(
      `select string_agg(action, ',' order by occurred_at) from public.audit_logs where entity_id in ('${groupId}', '${id}') and action in ('group.create', 'group.update', 'group.archive', 'member.move', 'member.role_change');`,
    ),
  ).toBe("group.create,member.move,member.role_change,group.update,group.update,member.move,group.archive");
});
