import { expect, test } from "@playwright/test";
import { LEADER_STATE, MEMBER, expectAccessible, signIn, sql, uniqueEmail } from "../helpers";

// A Cell Meeting that started two hours ago, so the guest can be marked. Titled "E2E " so teardown removes it and its invites.
const GROUP = "20000000-0000-0000-0000-000000000001";
function pastMeeting(title: string) {
  sql(`insert into public.events (church_id, event_type_id, group_id, title, starts_at, ends_at, occurs_on)
       select t.church_id, t.id, '${GROUP}', '${title}', now() - interval '2 hours', now() - interval '30 minutes', (now() at time zone 'Asia/Manila')::date
       from public.event_types t join public.cell_groups g on g.church_id = t.church_id and g.id = '${GROUP}' where t.code = 'CELL_MEETING';`);
}

test("a member logs a guest who came, and their leader registers the guest as a member", async ({ browser }) => {
  const suffix = Date.now().toString(36);
  const title = `E2E Cell Meeting ${suffix}`;
  const guest = `Carla Mendoza${suffix}`;
  pastMeeting(title);

  const memberCtx = await browser.newContext({ storageState: { cookies: [], origins: [] } });
  const member = await memberCtx.newPage();
  await signIn(member, MEMBER);
  await member.waitForURL("/");
  await member.goto("/invites");
  await expect(member.getByRole("heading", { name: "Invites", level: 1 })).toBeVisible();
  await expectAccessible(member);

  const option = await member.locator("option", { hasText: title }).getAttribute("value");
  await member.getByLabel("Event").selectOption(option!);
  await member.getByLabel("Guest's name").fill(guest);
  await member.getByLabel("Mobile or email (optional)").fill("0917 555 0199");
  await member.getByRole("button", { name: "Add guest" }).click();
  await expect(member.getByText(`${guest} added.`, { exact: false })).toBeVisible();

  const mine = member.getByRole("listitem").filter({ hasText: guest });
  await mine.getByRole("button", { name: "Came" }).click();
  await expect(mine.getByRole("button", { name: "Came" })).toHaveAttribute("aria-pressed", "true");
  await expectAccessible(member);

  // The leader sees the guest under their group, with the contact, and registers them.
  const leaderCtx = await browser.newContext({ storageState: LEADER_STATE });
  const leader = await leaderCtx.newPage();
  await leader.goto("/invites");
  const row = leader.getByRole("region", { name: "Your group's guests" }).getByRole("listitem").filter({ hasText: guest });
  await expect(row.getByText("0917 555 0199")).toBeVisible();
  await expect(row.getByText(/Invited by Nico/)).toBeVisible();
  await expectAccessible(leader);
  await row.getByRole("link", { name: "Register as member" }).click();

  await expect(leader.getByRole("heading", { name: "Register a member" })).toBeVisible();
  await expect(leader.getByLabel("First name")).toHaveValue("Carla");
  await expect(leader.getByLabel("Last name")).toHaveValue(`Mendoza${suffix}`);
  await leader.getByLabel("Gender").selectOption("FEMALE");
  await leader.getByLabel("Birthdate").fill("2000-02-14");
  await leader.getByLabel("Email", { exact: true }).fill(uniqueEmail("carla"));
  await leader
    .getByLabel("Mobile number", { exact: true })
    .fill(`0917 ${Math.floor(100 + Math.random() * 900)} ${Math.floor(1000 + Math.random() * 9000)}`);
  await leader.getByRole("button", { name: "Register" }).click();
  await expect(leader.getByRole("heading", { name: `Carla Mendoza${suffix}` })).toBeVisible();
  await leaderCtx.close();

  await member.reload();
  await expect(member.getByRole("listitem").filter({ hasText: guest }).getByText("Now a member")).toBeVisible();
  await memberCtx.close();
});
