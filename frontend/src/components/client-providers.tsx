"use client";

import { ThemeProvider, useTheme } from "next-themes";
import { useEffect } from "react";
import { Toaster } from "sonner";

const THEME_COLOR = { light: "#faf8f3", dark: "#0b0b0c" } as const;

// The viewport meta follows the OS; this keeps the browser bar in step with an explicit Light/Dark choice (§11.2).
function ThemeChrome() {
  const { resolvedTheme } = useTheme();
  useEffect(() => {
    if (resolvedTheme !== "light" && resolvedTheme !== "dark") return;
    document.querySelectorAll('meta[name="theme-color"]').forEach((m) => m.setAttribute("content", THEME_COLOR[resolvedTheme]));
  }, [resolvedTheme]);
  return <Toaster theme={resolvedTheme === "dark" ? "dark" : "light"} position="top-center" richColors closeButton />;
}

export function ClientProviders({ nonce, children }: { nonce?: string; children: React.ReactNode }) {
  return (
    <ThemeProvider attribute="class" defaultTheme="system" enableSystem disableTransitionOnChange nonce={nonce}>
      {children}
      <ThemeChrome />
    </ThemeProvider>
  );
}
