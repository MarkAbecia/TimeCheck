/** Problem Details from the API (field errors are keyed by camelCase field names). */
export type Problem = {
  status: number;
  title?: string;
  code?: string;
  errors?: Record<string, string[]>;
  matches?: { field: "mobile" | "nameBirthdate"; sameGroup: boolean }[];
};

export type ApiResult<T> = { ok: true; data: T } | { ok: false; problem: Problem };

/** Client-side call through the BFF proxy (/api/bff → /api/v1). Never throws. */
export async function send<T = unknown>(method: "POST" | "PATCH", path: string, body?: unknown): Promise<ApiResult<T>> {
  if (typeof navigator !== "undefined" && !navigator.onLine) return { ok: false, problem: { status: 0, code: "offline" } };
  try {
    const res = await fetch(`/api/bff${path}`, {
      method,
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(body ?? {}),
      credentials: "same-origin",
    });
    if (res.status === 401) {
      window.location.assign("/login");
      return { ok: false, problem: { status: 401 } };
    }
    const text = await res.text();
    const json = text ? JSON.parse(text) : undefined;
    return res.ok ? { ok: true, data: json as T } : { ok: false, problem: { status: res.status, ...(json ?? {}) } };
  } catch {
    return { ok: false, problem: { status: 0 } };
  }
}
