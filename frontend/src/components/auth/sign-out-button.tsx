"use client";

import { LogOut } from "lucide-react";
import { clearQueue } from "@/lib/offline-queue";
import { disablePush } from "@/lib/push";

/**
 * A plain form POST: works before hydration and with JS off. /api/auth/logout revokes the session and redirects
 * to /login. Attendance still queued offline is dropped and pushes to this browser stop, so the next person on a shared
 * phone neither sends the last person's marks nor sees their notifications.
 * "shell" is the icon button that sits beside the theme toggle on the black shell.
 */
export function SignOutButton({ label, className, variant = "link" }: { label: string; className?: string; variant?: "link" | "shell" }) {
  return (
    <form
      action="/api/auth/logout"
      method="post"
      className={className}
      onSubmit={(e) => {
        clearQueue();
        const form = e.currentTarget;
        e.preventDefault();
        void disablePush()
          .catch(() => undefined)
          .finally(() => form.submit());
      }}
    >
      {variant === "shell" ? (
        <button
          type="submit"
          aria-label={label}
          title={label}
          className="inline-flex size-11 items-center justify-center rounded-lg text-shell-foreground transition-colors hover:bg-white/10 focus-visible:ring-2 focus-visible:ring-ring focus-visible:outline-none"
        >
          <LogOut className="size-5" aria-hidden />
        </button>
      ) : (
        <button
          type="submit"
          className="inline-flex min-h-11 items-center text-sm font-medium text-muted-foreground underline-offset-4 hover:text-foreground hover:underline focus-visible:ring-2 focus-visible:ring-ring focus-visible:outline-none"
        >
          {label}
        </button>
      )}
    </form>
  );
}
