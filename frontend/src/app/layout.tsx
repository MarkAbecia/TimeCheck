import type { Metadata, Viewport } from "next";
import { Barlow_Condensed, Public_Sans } from "next/font/google";
import { Providers } from "@/components/providers";
import "./globals.css";

// next/font self-hosts both families at build time; no request to Google at runtime.
const publicSans = Public_Sans({ subsets: ["latin"], variable: "--font-public-sans", display: "swap" });
const barlowCondensed = Barlow_Condensed({
  subsets: ["latin"],
  weight: ["600", "700"],
  variable: "--font-barlow-condensed",
  display: "swap",
});

export const metadata: Metadata = {
  title: { default: "TimeCheck", template: "%s · TimeCheck" },
  description: "Cell group attendance and member care for your church.",
  applicationName: "TimeCheck",
  robots: { index: false, follow: false },
};

export const viewport: Viewport = {
  width: "device-width",
  initialScale: 1,
  viewportFit: "cover",
  themeColor: [
    { media: "(prefers-color-scheme: light)", color: "#faf8f3" },
    { media: "(prefers-color-scheme: dark)", color: "#0b0b0c" },
  ],
};

export default function RootLayout({ children }: LayoutProps<"/">) {
  return (
    <html lang="en" suppressHydrationWarning className={`${publicSans.variable} ${barlowCondensed.variable}`}>
      <body className="min-h-dvh">
        {/* Providers read the per-request CSP nonce, which makes every page dynamic (ADR 0005). */}
        <Providers>{children}</Providers>
      </body>
    </html>
  );
}
