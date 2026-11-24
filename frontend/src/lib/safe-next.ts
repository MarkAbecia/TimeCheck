/** Only same-origin paths survive as a post-login redirect; anything else (//evil, https://…, /\evil) goes home. */
export function safeNext(next: string | null | undefined): string {
  if (!next || !next.startsWith("/") || next.startsWith("//") || next.includes("\\")) return "/";
  try {
    const url = new URL(next, "http://x");
    return url.origin === "http://x" ? `${url.pathname}${url.search}${url.hash}` : "/";
  } catch {
    return "/";
  }
}
