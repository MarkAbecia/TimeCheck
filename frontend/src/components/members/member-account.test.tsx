import { screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { renderWithIntl } from "@/test/intl";
import type { Member } from "@/lib/types";
import { MemberAccount } from "./member-account";

const push = vi.fn();
const refresh = vi.fn();
vi.mock("next/navigation", () => ({ useRouter: () => ({ push, refresh }) }));
vi.mock("sonner", () => ({ toast: { success: vi.fn(), error: vi.fn() } }));

const fetchMock = vi.fn();
beforeEach(() => {
  vi.stubGlobal("fetch", fetchMock);
  fetchMock.mockReset();
  push.mockReset();
});
afterEach(() => vi.unstubAllGlobals());

const member = (isActive: boolean) => ({ id: "m1", displayName: "Erin Cruz", groupId: "g1", isActive }) as Member;

describe("MemberAccount", () => {
  it("asks before deactivating, and only erases a deactivated person", async () => {
    const user = userEvent.setup();
    renderWithIntl(<MemberAccount member={member(true)} />);
    expect(screen.queryByRole("button", { name: "Erase for good" })).not.toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Deactivate" }));
    expect(fetchMock).not.toHaveBeenCalled();
    expect(screen.getByText(/won't be able to sign in/)).toBeInTheDocument();

    fetchMock.mockResolvedValue(new Response(JSON.stringify({ ...member(false) }), { status: 200 }));
    await user.click(screen.getByRole("button", { name: "Yes, deactivate" }));
    expect(fetchMock).toHaveBeenCalledWith(
      "/api/bff/members/m1/active",
      expect.objectContaining({ method: "POST", body: JSON.stringify({ active: false }) }),
    );
  });

  it("erases only after the person's name is typed exactly", async () => {
    const user = userEvent.setup();
    renderWithIntl(<MemberAccount member={member(false)} />);
    const erase = screen.getByRole("button", { name: "Erase for good" });
    expect(erase).toBeDisabled();

    await user.type(screen.getByLabelText("Type Erin Cruz to confirm"), "Erin");
    expect(erase).toBeDisabled();
    await user.type(screen.getByLabelText("Type Erin Cruz to confirm"), " Cruz");
    expect(erase).toBeEnabled();

    fetchMock.mockResolvedValue(new Response(null, { status: 204 }));
    await user.click(erase);
    expect(fetchMock).toHaveBeenCalledWith("/api/bff/members/m1/erase", expect.objectContaining({ method: "POST" }));
    expect(push).toHaveBeenCalledWith("/members?group=g1");
  });

  it("shows the API's reason when deactivation is refused", async () => {
    const user = userEvent.setup();
    renderWithIntl(<MemberAccount member={member(true)} />);
    fetchMock.mockResolvedValue(
      new Response(JSON.stringify({ title: "They lead Joshua Cell. Choose another leader for it first." }), { status: 422 }),
    );
    await user.click(screen.getByRole("button", { name: "Deactivate" }));
    await user.click(screen.getByRole("button", { name: "Yes, deactivate" }));
    expect(await screen.findByRole("alert")).toHaveTextContent("They lead Joshua Cell");
  });
});
