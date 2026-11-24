# 0005 — Frontend CSP and rendering

Status: Accepted (2026-11-24)

**Decision: a strict nonce CSP, so every page renders dynamically. Cache Components / Partial Prerendering stay off.**

- `src/proxy.ts` makes a fresh nonce for each request. It sends it in the `Content-Security-Policy` header with
  `script-src 'self' 'nonce-…' 'strict-dynamic'` and no `unsafe-inline` for scripts. Next stamps the nonce on its own
  scripts. next-themes gets it through `x-nonce`.
- Next's docs state that PPR does not work with nonces: a prerendered static shell can't carry a per-request nonce.
  We tried it with `cacheComponents: true`, and production blocked every framework script.
- The cost is small here. Every screen except the sign-in pages is per-user and authenticated, so none of them could
  be served from a CDN anyway.
- Styles:
  - `style-src 'self' 'nonce-…'` covers stylesheets.
  - It also allows two hashes. One is the exact stylesheet sonner injects; `src/lib/csp.test.ts` fails if a sonner
    upgrade changes it. The other is an empty `<style>`.
  - `style-src-attr 'unsafe-inline'` is allowed because React renders `style=""` attributes (Base UI positioning,
    sonner), and attributes can't carry a nonce. Inline style attributes can't run script, so the risk is limited to
    CSS-based UI redress.
- **Alternative rejected:** SRI (experimental) with PPR. Next's inline RSC payload scripts would still need
  `'unsafe-inline'` for scripts, which is weaker than what we have.
- **Revisit** if Next ships nonce support for PPR shells.

Other headers set in the proxy:

- nosniff
- `Referrer-Policy: strict-origin-when-cross-origin`
- `X-Frame-Options: DENY` plus `frame-ancestors 'none'`
- `Permissions-Policy` (camera allowed for self only, because of devotion uploads)
- COOP same-origin
- HSTS outside development

Fonts (Public Sans, Barlow Condensed) are self-hosted by `next/font` at build time, so `font-src 'self'` is enough.
