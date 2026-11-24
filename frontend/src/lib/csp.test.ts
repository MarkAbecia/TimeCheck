// @vitest-environment node
import { createHash } from "node:crypto";
import { readFileSync } from "node:fs";
import { describe, expect, it } from "vitest";
import { NextRequest } from "next/server";
import { proxy } from "@/proxy";

async function csp() {
  return (await proxy(new NextRequest("http://localhost/login"))).headers.get("Content-Security-Policy")!;
}

describe("proxy CSP", () => {
  it("issues a fresh nonce per request and no unsafe-inline for scripts", async () => {
    const a = await csp();
    const b = await csp();
    const nonce = /'nonce-([^']+)'/.exec(a)?.[1];
    expect(nonce).toBeTruthy();
    expect(b).not.toContain(nonce);
    expect(a).toMatch(/script-src 'self' 'nonce-[^']+' 'strict-dynamic'/);
    expect(a).not.toMatch(/script-src[^;]*unsafe-inline/);
    expect(a).toContain("frame-ancestors 'none'");
  });

  it("allows the exact stylesheet sonner injects (fails after a sonner upgrade until the hash is updated)", async () => {
    const src = readFileSync("node_modules/sonner/dist/index.mjs", "utf8");
    const literal = /__insertCSS\(("(?:[^"\\]|\\.)*")\)/.exec(src)?.[1];
    expect(literal).toBeTruthy();
    const hash = createHash("sha256").update(JSON.parse(literal!)).digest("base64");
    expect(await csp()).toContain(`'sha256-${hash}'`);
  });
});
