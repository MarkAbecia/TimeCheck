import { Monitor, Moon, Sun } from "lucide-react";
import { useSyncExternalStore } from "react";

export const THEME_OPTIONS = [
  { value: "light", icon: Sun },
  { value: "dark", icon: Moon },
  { value: "system", icon: Monitor },
] as const;

const subscribe = () => () => {};
/** True only after hydration, so the stored choice never causes a server/client mismatch. */
export const useHydrated = () =>
  useSyncExternalStore(
    subscribe,
    () => true,
    () => false,
  );

export const themeTriggerClass =
  "inline-flex size-11 items-center justify-center rounded-lg text-shell-foreground transition-colors hover:bg-white/10";

/** CSS picks the icon so the first paint is right before JS knows the theme. */
export function ThemeIcon() {
  return (
    <>
      <Sun className="size-5 dark:hidden" aria-hidden />
      <Moon className="hidden size-5 dark:block" aria-hidden />
    </>
  );
}
