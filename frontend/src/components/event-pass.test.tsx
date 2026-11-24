import { screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { renderWithIntl } from "@/test/intl";
import { EventPass, type Pass, type PassStub } from "./event-pass";

const pass = (stub: PassStub): Pass => ({
  id: "p",
  kind: "group",
  timeLabel: "7:30 – 9:30 PM",
  name: "Cell Meeting",
  meta: "Joshua Cell",
  stub,
});

describe("EventPass stub states", () => {
  it("todo: the stub is the check-in link, named by its visible words then the event", () => {
    renderWithIntl(<EventPass pass={pass({ state: "todo", total: 20, href: "/events/p" })} />);
    const link = screen.getByRole("link", { name: "Check in 0 of 20 for Cell Meeting" });
    expect(link).toHaveAttribute("href", "/events/p");
    expect(screen.getByText("0 of 20")).toBeInTheDocument();
  });

  it("done: shows the count and stays editable", () => {
    renderWithIntl(<EventPass pass={pass({ state: "done", present: 18, total: 20, href: "/events/p" })} />);
    expect(screen.getByText("18/20")).toBeInTheDocument();
    expect(screen.getByText("Marked")).toBeInTheDocument();
    expect(screen.getByRole("link")).toHaveAttribute("href", "/events/p");
  });

  it("upcoming, attended and pending are not actionable", () => {
    for (const stub of [{ state: "upcoming", day: "Fri", time: "7 PM" }, { state: "attended" }, { state: "pending" }] as PassStub[]) {
      const { unmount } = renderWithIntl(<EventPass pass={pass(stub)} />);
      expect(screen.queryByRole("link")).toBeNull();
      unmount();
    }
    renderWithIntl(<EventPass pass={pass({ state: "upcoming", day: "Fri", time: "7 PM" })} />);
    expect(screen.getByText("opens")).toBeInTheDocument();
    expect(screen.getByText("7 PM")).toBeInTheDocument();
  });

  it("is a labelled article with the event kind for screen readers", () => {
    renderWithIntl(<EventPass pass={{ ...pass({ state: "pending" }), kind: "church" }} />);
    expect(screen.getByRole("article", { name: "Cell Meeting" })).toHaveTextContent("Church-wide event");
  });
});
