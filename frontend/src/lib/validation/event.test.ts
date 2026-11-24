import { describe, expect, it } from "vitest";
import { toPass } from "../passes";
import { timeRange, weekStart } from "../time";
import type { EventSummary } from "../types";
import { eventSchema, toEventBody } from "./event";

const valid = {
  eventTypeId: "t1",
  groupId: "g1",
  title: "",
  date: "2026-11-27",
  start: "19:30",
  end: "21:30",
  endsNextDay: false,
  location: "Cruz home",
  onlineLink: "",
  description: "",
  repeatWeeks: "4",
  audienceGroupIds: [],
};

describe("event form", () => {
  it("turns local times into Manila instants and blanks into nulls", () => {
    const body = toEventBody(eventSchema.parse(valid));
    expect(body).toMatchObject({ startsAt: "2026-11-27T19:30:00+08:00", endsAt: "2026-11-27T21:30:00+08:00", title: null, repeatWeeks: 4 });
  });

  it("runs past midnight only when asked", () => {
    expect(eventSchema.safeParse({ ...valid, start: "22:00", end: "02:00" }).error?.issues[0].message).toBe("endBeforeStart");
    const body = toEventBody(eventSchema.parse({ ...valid, start: "22:00", end: "02:00", endsNextDay: true }));
    expect(body.endsAt).toBe("2026-11-28T02:00:00+08:00");
  });

  it("wants https links and at most 26 weeks", () => {
    const r = eventSchema.safeParse({ ...valid, onlineLink: "http://zoom.us/j/1", repeatWeeks: "30" });
    expect(Object.fromEntries(r.error!.issues.map((i) => [i.path[0], i.message]))).toEqual({
      onlineLink: "linkInvalid",
      repeatWeeks: "repeatTooMany",
    });
  });
});

describe("time and passes", () => {
  it("formats Manila time ranges", () => {
    expect(timeRange("2026-11-23T21:00:00Z", "2026-11-23T22:00:00Z")).toBe("5:00 – 6:00 AM");
    expect(timeRange("2026-11-24T03:00:00Z", "2026-11-24T05:00:00Z")).toBe("11:00 AM – 1:00 PM");
    expect(weekStart("2026-11-29")).toBe("2026-11-23"); // Sunday belongs to the week that began Monday
    expect(weekStart("2026-11-23")).toBe("2026-11-23");
  });

  const e: EventSummary = {
    id: "e1",
    typeCode: "CELL_MEETING",
    typeName: "Cell Meeting",
    category: "WEEKLY",
    groupId: "g1",
    groupName: "Joshua Cell",
    title: "Cell Meeting",
    startsAt: "2026-11-24T11:30:00Z",
    endsAt: "2026-11-24T13:30:00Z",
    occursOn: "2026-11-24",
    location: "Cruz home",
    isCancelled: false,
    isChurchWide: false,
    myStatus: null,
    marking: null,
  };
  const t = { members: (n: number) => `${n} members` };

  it("gives leaders the check-in stub until someone is marked", () => {
    expect(toPass({ ...e, marking: { expected: 20, marked: 0, present: 0 } }, "today", t).stub).toEqual({
      state: "todo",
      total: 20,
      href: "/events/e1",
    });
    expect(toPass({ ...e, marking: { expected: 20, marked: 20, present: 18 } }, "today", t)).toMatchObject({
      meta: "Joshua Cell · 20 members",
      stub: { state: "done", present: 18, total: 20 },
    });
  });

  it("shows members their own mark", () => {
    expect(toPass({ ...e, myStatus: "LATE" }, "today", t).stub).toEqual({ state: "attended" });
    expect(toPass(e, "today", t)).toMatchObject({ meta: "Joshua Cell · Cruz home", stub: { state: "pending" }, kind: "group" });
    expect(toPass({ ...e, groupId: null, groupName: null }, "upcoming", t)).toMatchObject({
      kind: "church",
      stub: { state: "upcoming", day: "Tue", time: "7:30 PM" },
    });
  });
});
