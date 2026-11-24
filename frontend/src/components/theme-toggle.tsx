"use client";

import { Monitor, Moon, Sun } from "lucide-react";
import { useTranslations } from "next-intl";
import { useTheme } from "next-themes";
import { useSyncExternalStore } from "react";
import { cn } from "@/lib/utils";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuGroup,
  DropdownMenuLabel,
  DropdownMenuRadioGroup,
  DropdownMenuRadioItem,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";

const OPTIONS = [
  { value: "light", icon: Sun },
  { value: "dark", icon: Moon },
  { value: "system", icon: Monitor },
] as const;

const subscribe = () => () => {};
// True only after hydration, so the stored choice never causes a server/client mismatch.
const useHydrated = () => useSyncExternalStore(subscribe, () => true, () => false);

/** Header control: an icon button opening Light / Dark / System. Lives on the black shell. */
export function ThemeToggle({ className }: { className?: string }) {
  const t = useTranslations("theme");
  const { theme, setTheme } = useTheme();
  const hydrated = useHydrated();

  return (
    <DropdownMenu>
      <DropdownMenuTrigger
        aria-label={t("label")}
        className={cn(
          "inline-flex size-11 items-center justify-center rounded-lg text-shell-foreground transition-colors hover:bg-white/10",
          className,
        )}
      >
        {/* CSS picks the icon so the first paint is right before JS knows the theme. */}
        <Sun className="size-5 dark:hidden" aria-hidden />
        <Moon className="hidden size-5 dark:block" aria-hidden />
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end" className="min-w-44">
        <DropdownMenuGroup>
          <DropdownMenuLabel>{t("label")}</DropdownMenuLabel>
          <DropdownMenuRadioGroup value={hydrated ? (theme ?? "system") : "system"} onValueChange={(v) => setTheme(String(v))}>
            {OPTIONS.map(({ value, icon: Icon }) => (
              <DropdownMenuRadioItem key={value} value={value} className="min-h-11 gap-2.5">
                <Icon className="size-4" aria-hidden />
                {t(value)}
              </DropdownMenuRadioItem>
            ))}
          </DropdownMenuRadioGroup>
        </DropdownMenuGroup>
      </DropdownMenuContent>
    </DropdownMenu>
  );
}

/** Profile → Appearance: the same three choices as a segmented control (native radios, so arrow keys work). */
export function ThemeSegmented() {
  const t = useTranslations("theme");
  const { theme, setTheme } = useTheme();
  const hydrated = useHydrated();
  const current = hydrated ? (theme ?? "system") : undefined;

  return (
    <fieldset className="inline-grid grid-cols-3 gap-1 rounded-lg bg-muted p-1">
      <legend className="sr-only">{t("label")}</legend>
      {OPTIONS.map(({ value, icon: Icon }) => (
        <label
          key={value}
          className="inline-flex min-h-11 cursor-pointer items-center justify-center gap-2 rounded-md px-4 text-sm font-medium text-muted-foreground transition-colors has-checked:bg-card has-checked:text-foreground has-checked:shadow-sm has-focus-visible:outline-2 has-focus-visible:outline-ring"
        >
          <input
            type="radio"
            name="theme"
            value={value}
            checked={current === value}
            onChange={() => setTheme(value)}
            className="sr-only"
          />
          <Icon className="size-4" aria-hidden />
          {t(value)}
        </label>
      ))}
    </fieldset>
  );
}
