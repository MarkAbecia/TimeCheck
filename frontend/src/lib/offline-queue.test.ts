import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { flushQueue, pendingCount, pendingFor, queueMarks } from "./offline-queue";

const mark = (userId: string, status: "PRESENT" | "ABSENT") => ({ userId, status, note: null, rowVersion: null });
const respond = (status: number) => vi.fn(async () => new Response(status === 200 ? "{}" : JSON.stringify({ status }), { status }));

describe("offline attendance queue", () => {
  beforeEach(() => localStorage.clear());
  afterEach(() => vi.unstubAllGlobals());

  it("keeps the latest mark per person", () => {
    queueMarks("e1", [mark("a", "PRESENT"), mark("b", "PRESENT")]);
    queueMarks("e1", [mark("b", "ABSENT")]);
    expect(pendingFor("e1")).toEqual([mark("a", "PRESENT"), mark("b", "ABSENT")]);
    expect(pendingCount()).toBe(2);
  });

  it("sends and clears once online", async () => {
    queueMarks("e1", [mark("a", "PRESENT")]);
    const fetch = respond(200);
    vi.stubGlobal("fetch", fetch);
    expect(await flushQueue()).toEqual({ sent: 1, clashed: [], kept: 0 });
    expect(fetch).toHaveBeenCalledWith("/api/bff/events/e1/attendance", expect.objectContaining({ method: "PUT" }));
    expect(pendingCount()).toBe(0);
  });

  it("keeps marks through server errors and drops clashes", async () => {
    queueMarks("e1", [mark("a", "PRESENT")]);
    queueMarks("e2", [mark("b", "PRESENT")]);
    vi.stubGlobal(
      "fetch",
      vi.fn(async (url: string) => new Response("{}", { status: url.includes("e1") ? 503 : 409 })),
    );
    expect(await flushQueue()).toEqual({ sent: 0, clashed: ["e2"], kept: 1 });
    expect(pendingFor("e1")).toHaveLength(1);
    expect(pendingFor("e2")).toHaveLength(0);
  });
});
