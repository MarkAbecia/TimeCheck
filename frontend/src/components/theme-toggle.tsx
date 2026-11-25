"use client";

import { useTranslations } from "next-intl";
import { useTheme } from "next-themes";
import { useState } from "react";
import { cn } from "@/lib/utils";
import { THEME_OPTIONS, ThemeIcon, themeTriggerClass, useHydrated } from "./theme-options";

type Menu = typeof import("./theme-menu").ThemeMenu;

/**
 * Header control: an icon button opening Light / Dark / System. Lives on the black shell. The menu and its positioning library
 * load on the first tap and then replace this button, opened (§9 JS budget: they'd otherwise ship with every page).
 */
export function ThemeToggle({ className }: { className?: string }) {
  const t = useTranslations("theme");
  const [Menu, setMenu] = useState<Menu | null>(null);
  if (Menu) return <Menu className={className} defaultOpen />;
  return (
    <button
      type="button"
      aria-label={t("label")}
      aria-haspopup="menu"
      aria-expanded={false}
      className={cn(themeTriggerClass, className)}
      onClick={() => void import("./theme-menu").then((m) => setMenu(() => m.ThemeMenu))}
    >
      <ThemeIcon />
    </button>
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
      {THEME_OPTIONS.map(({ value, icon: Icon }) => (
        <label
          key={value}
          className="inline-flex min-h-11 cursor-pointer items-center justify-center gap-2 rounded-md px-4 text-sm font-medium text-muted-foreground transition-colors has-checked:bg-card has-checked:text-foreground has-checked:shadow-sm has-focus-visible:outline-2 has-focus-visible:outline-ring"
        >
          <input type="radio" name="theme" value={value} checked={current === value} onChange={() => setTheme(value)} className="sr-only" />
          <Icon className="size-4" aria-hidden />
          {t(value)}
        </label>
      ))}
    </fieldset>
  );
}
