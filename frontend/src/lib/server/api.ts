import "server-only";
import { cookies } from "next/headers";
import { notFound, redirect } from "next/navigation";
import { callApi } from "./bff";
import { ACCESS_COOKIE } from "./tokens";

/** GET from the API for a server component. 401 → sign-in, 403/404 → not found (no hint that it exists). */
export async function apiGet<T>(path: string): Promise<T> {
  const token = (await cookies()).get(ACCESS_COOKIE)?.value;
  if (!token) redirect("/login");
  const res = await callApi(`/api/v1${path}`, token);
  if (res.status === 401) redirect("/login");
  if (res.status === 403 || res.status === 404) notFound();
  if (!res.ok) throw new Error(`GET ${path} failed with ${res.status}`);
  return (await res.json()) as T;
}
