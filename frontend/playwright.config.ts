import { defineConfig, devices } from "@playwright/test";

// Needs the full local stack: Supabase (`supabase start`), Redis, and the API on :5170. See README → End-to-end tests.
const baseURL = process.env.E2E_BASE_URL ?? "http://localhost:3400";
// The §13 viewports; axe runs at each in light and dark.
const VIEWPORTS = [
  { width: 360, height: 800 },
  { width: 390, height: 844 },
  { width: 768, height: 1024 },
  { width: 1024, height: 768 },
  { width: 1440, height: 900 },
];
const browser = { ...devices["Desktop Chrome"], channel: process.env.PW_CHANNEL ?? "chrome" }; // local Chrome; set PW_CHANNEL= for bundled Chromium

export default defineConfig({
  testDir: "./e2e",
  outputDir: "./test-results",
  workers: 1, // the specs share one database and the login rate limit
  retries: 0,
  reporter: [["list"], ["html", { open: "never" }]],
  globalTeardown: "./e2e/teardown.ts",
  use: { baseURL, trace: "retain-on-failure", screenshot: "only-on-failure" },
  projects: [
    { name: "setup", testMatch: /auth\.setup\.ts/, use: browser },
    {
      name: "flows",
      testMatch: /flows\/.*\.spec\.ts/,
      dependencies: ["setup"],
      use: { ...browser, viewport: { width: 390, height: 844 }, isMobile: false, hasTouch: true },
    },
    ...VIEWPORTS.flatMap((viewport) =>
      (["light", "dark"] as const).map((colorScheme) => ({
        name: `a11y-${viewport.width}-${colorScheme}`,
        testMatch: /a11y\.spec\.ts/,
        dependencies: ["setup"],
        use: { ...browser, viewport, colorScheme },
      })),
    ),
  ],
  webServer: {
    command: "npm run dev -- --port 3400",
    url: `${baseURL}/api/health`,
    reuseExistingServer: true,
    timeout: 120_000,
  },
});
