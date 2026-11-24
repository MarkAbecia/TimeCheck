// WCAG 2.2 AA contrast check over the palette tokens in src/app/globals.css (REQUIREMENTS §11.1).
// Runs in CI (`npm run contrast`) so a palette tweak can't silently regress accessibility.
import { readFileSync } from "node:fs";

const css = readFileSync(new URL("../src/app/globals.css", import.meta.url), "utf8");

function block(selector) {
  const start = css.indexOf(`${selector} {`);
  if (start < 0) throw new Error(`No ${selector} block in globals.css`);
  const body = css.slice(start, css.indexOf("}", start));
  return Object.fromEntries([...body.matchAll(/--([a-z0-9-]+):\s*(#[0-9a-f]{6})\b/gi)].map((m) => [m[1], m[2]]));
}

const light = block(":root");
const dark = { ...light, ...block(".dark") }; // .dark only overrides what changes

function luminance(hex) {
  const [r, g, b] = [1, 3, 5].map((i) => parseInt(hex.slice(i, i + 2), 16) / 255)
    .map((c) => (c <= 0.03928 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4));
  return 0.2126 * r + 0.7152 * g + 0.0722 * b;
}
export function ratio(a, b) {
  const [l1, l2] = [luminance(a), luminance(b)].sort((x, y) => y - x);
  return (l1 + 0.05) / (l2 + 0.05);
}

const TEXT = 4.5, UI = 3; // body text / non-text UI (focus rings, bands, borders that carry meaning)
const pairs = [
  ["foreground", "background", TEXT], ["foreground", "card", TEXT], ["foreground", "surface-2", TEXT],
  ["muted-foreground", "background", TEXT], ["muted-foreground", "card", TEXT], ["muted-foreground", "surface-2", TEXT],
  ["primary-foreground", "primary", TEXT],
  ["primary-text", "background", TEXT], ["primary-text", "card", TEXT],
  ["accent-text", "background", TEXT], ["accent-text", "card", TEXT],
  ["accent-foreground", "accent", TEXT],
  ["destructive", "background", TEXT], ["destructive", "card", TEXT],
  ["success", "card", TEXT], ["warning", "card", TEXT],
  ["shell-foreground", "shell", TEXT], ["shell-muted", "shell", TEXT], ["shell-active", "shell", TEXT],
  ["accent-foreground", "shell-accent", TEXT],
  ["ring", "background", UI], ["ring", "card", UI], ["shell-ring", "shell", UI],
  ["band-group", "card", UI], ["band-church", "card", UI],
  ["foreground", "stub-fill", TEXT], ["muted-foreground", "stub-fill", TEXT],
];

let failed = 0;
for (const [mode, tokens] of [["light", light], ["dark", dark]]) {
  for (const [fg, bg, min] of pairs) {
    if (!tokens[fg] || !tokens[bg]) { console.error(`✗ ${mode}: missing --${fg} or --${bg}`); failed++; continue; }
    const r = ratio(tokens[fg], tokens[bg]);
    const ok = r >= min;
    if (!ok) failed++;
    console.log(`${ok ? "✓" : "✗"} ${mode.padEnd(5)} ${fg} on ${bg}: ${r.toFixed(2)}:1 (min ${min})`);
  }
}
if (failed) { console.error(`\n${failed} contrast pair(s) below WCAG AA.`); process.exit(1); }
console.log("\nAll contrast pairs pass WCAG 2.2 AA.");
