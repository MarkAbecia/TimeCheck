import { defineConfig, devices } from "@playwright/test";

// Smoke tests against a deployed environment (REQUIREMENTS §14 Phase 12): `SMOKE_BASE_URL=https://… npm run smoke`.
// Read-only apart from signing in, so they're safe on production. No local stack, database access or web server.
const baseURL = process.env.SMOKE_BASE_URL;
if (!baseURL) throw new Error("Set SMOKE_BASE_URL to the web service's public URL.");

export default defineConfig({
  testDir: "./e2e/smoke",
  outputDir: "./test-results/smoke",
  workers: 1, // the sign-in rate limit is per account
  retries: 1, // a cold instance after a deploy can miss the first request
  reporter: [["list"]],
  use: { baseURL, trace: "retain-on-failure", screenshot: "only-on-failure" },
  projects: [{ name: "smoke", use: { ...devices["Desktop Chrome"], channel: process.env.PW_CHANNEL || undefined } }],
});
