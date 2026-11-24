import { screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { renderWithIntl } from "@/test/intl";
import { LoginForm } from "./login-form";

const replace = vi.fn();
vi.mock("next/navigation", () => ({
  useRouter: () => ({ replace }),
  useSearchParams: () => new URLSearchParams("next=//evil.example"),
}));

const fetchMock = vi.fn();
beforeEach(() => {
  vi.stubGlobal("fetch", fetchMock);
  fetchMock.mockReset();
  replace.mockReset();
});
afterEach(() => vi.unstubAllGlobals());

async function fillAndSubmit(email = "leader@timecheck.test", password = "correct-horse-1") {
  const user = userEvent.setup();
  await user.type(screen.getByLabelText("Email"), email);
  await user.type(screen.getByLabelText("Password"), password);
  await user.click(screen.getByRole("button", { name: "Sign in" }));
}

describe("LoginForm", () => {
  it("shows field errors and does not call the server for invalid input", async () => {
    renderWithIntl(<LoginForm />);
    await userEvent.setup().click(screen.getByRole("button", { name: "Sign in" }));
    expect(await screen.findByText("Enter your email.")).toBeInTheDocument();
    expect(screen.getByText("Enter your password.")).toBeInTheDocument();
    expect(screen.getByLabelText("Email")).toHaveAttribute("aria-invalid", "true");
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("posts lower-cased email to the BFF and redirects to a safe path", async () => {
    fetchMock.mockResolvedValue(new Response(null, { status: 204 }));
    renderWithIntl(<LoginForm />);
    await fillAndSubmit("Leader@TimeCheck.test");
    await waitFor(() => expect(replace).toHaveBeenCalledWith("/"));
    const [url, init] = fetchMock.mock.calls[0];
    expect(url).toBe("/api/auth/login");
    expect(JSON.parse(init.body)).toEqual({ email: "leader@timecheck.test", password: "correct-horse-1" });
  });

  it.each([
    [401, "That email and password don't match. Try again, or reset your password."],
    [429, "Too many tries. Wait a minute, then try again."],
    [500, "Something went wrong on our side. Try again in a moment."],
  ])("maps HTTP %i to a friendly alert", async (status, message) => {
    fetchMock.mockResolvedValue(new Response(null, { status }));
    renderWithIntl(<LoginForm />);
    await fillAndSubmit();
    expect(await screen.findByRole("alert")).toHaveTextContent(message);
    expect(replace).not.toHaveBeenCalled();
  });

  it("toggles password visibility", async () => {
    renderWithIntl(<LoginForm />);
    const user = userEvent.setup();
    const input = screen.getByLabelText("Password");
    expect(input).toHaveAttribute("type", "password");
    await user.click(screen.getByRole("button", { name: "Show password" }));
    expect(input).toHaveAttribute("type", "text");
  });
});
