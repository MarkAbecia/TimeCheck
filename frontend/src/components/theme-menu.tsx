"use client";

import { useTranslations } from "next-intl";
import { useTheme } from "next-themes";
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
import { THEME_OPTIONS, ThemeIcon, themeTriggerClass, useHydrated } from "./theme-options";

/** The Light / Dark / System menu. Loaded on first tap by ThemeToggle, so its menu library stays off every page load. */
export function ThemeMenu({ className, defaultOpen }: { className?: string; defaultOpen?: boolean }) {
  const t = useTranslations("theme");
  const { theme, setTheme } = useTheme();
  const hydrated = useHydrated();

  return (
    <DropdownMenu defaultOpen={defaultOpen}>
      <DropdownMenuTrigger aria-label={t("label")} className={cn(themeTriggerClass, className)}>
        <ThemeIcon />
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end" className="min-w-44">
        <DropdownMenuGroup>
          <DropdownMenuLabel>{t("label")}</DropdownMenuLabel>
          <DropdownMenuRadioGroup value={hydrated ? (theme ?? "system") : "system"} onValueChange={(v) => setTheme(String(v))}>
            {THEME_OPTIONS.map(({ value, icon: Icon }) => (
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
