import { describe, expect, it } from "vitest";
import { isActive, navFor, PHONE_TABS, type Role } from "./nav";
import { safeNext } from "./safe-next";

const keys = (items: { key: string }[]) => items.map((i) => i.key);

describe("navFor", () => {
  it.each<Role>(["member", "leader", "admin", "super_admin"])("%s gets at most 5 phone slots including More", (role) => {
    const { tabs, more, all } = navFor(role);
    expect(tabs.length + (more.length ? 1 : 0)).toBeLessThanOrEqual(PHONE_TABS + 1);
    expect([...tabs, ...more]).toEqual(all);
    expect(tabs[0].key).toBe("today");
  });

  it("only shows each role what §4.2 lets it do", () => {
    expect(keys(navFor("member").all)).not.toContain("members");
    expect(keys(navFor("member").all)).not.toContain("reports");
    expect(keys(navFor("leader").all)).not.toContain("groups");
    expect(keys(navFor("admin").all)).not.toContain("audit");
    expect(keys(navFor("admin").all)).not.toContain("settings");
    expect(keys(navFor("super_admin").all)).toEqual(expect.arrayContaining(["audit", "settings"]));
  });

  it("matches the active section by path prefix, Today only exactly", () => {
    expect(isActive("/", "/")).toBe(true);
    expect(isActive("/", "/members")).toBe(false);
    expect(isActive("/members", "/members/42")).toBe(true);
    expect(isActive("/members", "/membersx")).toBe(false);
  });
});

describe("safeNext", () => {
  it.each([
    [null, "/"],
    ["/members?tab=1", "/members?tab=1"],
    ["//evil.example", "/"],
    ["https://evil.example", "/"],
    ["/\\evil.example", "/"],
    ["members", "/"],
  ])("%s → %s", (input, expected) => expect(safeNext(input)).toBe(expected));
});
