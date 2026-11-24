import "server-only";
import type { Role } from "./nav";

export type Session = {
  firstName: string;
  initials: string;
  role: Role;
  groupName: string | null;
  churchName: string;
};

const ROLES: Role[] = ["super_admin", "admin", "leader", "member"];

// shortcut: demo session until the BFF cookie flow lands in Phase 4; then this reads /api/me with the session cookie.
export async function getSession(): Promise<Session> {
  const envRole = process.env.TC_DEMO_ROLE as Role | undefined;
  const role = envRole && ROLES.includes(envRole) ? envRole : "leader";
  return {
    firstName: "Joy",
    initials: "JD",
    role,
    groupName: role === "leader" || role === "member" ? "Joshua Cell" : null,
    churchName: "Demo Church",
  };
}

// shortcut: public branding is a constant until church settings ship (Phase 10 admin); then GET /api/v1/church/branding.
export async function getChurchBranding() {
  return { name: "Demo Church" };
}
