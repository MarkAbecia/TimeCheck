"use client";

import { send } from "@/lib/api-client";

/** Web push needs a service worker, the Push API and notification permission (iPhone: only once added to the home screen). */
export const pushSupported = () =>
  typeof window !== "undefined" && "serviceWorker" in navigator && "PushManager" in window && "Notification" in window;

export async function currentSubscription(): Promise<PushSubscription | null> {
  if (!pushSupported()) return null;
  const reg = await navigator.serviceWorker.getRegistration("/");
  return reg ? reg.pushManager.getSubscription() : null;
}

// The VAPID public key comes base64url-encoded; the Push API wants the raw bytes.
function keyBytes(base64url: string) {
  const b64 = (base64url + "=".repeat((4 - (base64url.length % 4)) % 4)).replace(/-/g, "+").replace(/_/g, "/");
  return Uint8Array.from(atob(b64), (c) => c.charCodeAt(0));
}

/** Asks permission, subscribes this browser and tells the API. */
export async function enablePush(publicKey: string): Promise<"on" | "denied" | "failed"> {
  if (!pushSupported()) return "failed";
  if ((await Notification.requestPermission()) !== "granted") return "denied";
  try {
    const reg = await navigator.serviceWorker.register("/sw.js");
    await navigator.serviceWorker.ready;
    const sub = await reg.pushManager.subscribe({ userVisibleOnly: true, applicationServerKey: keyBytes(publicKey) });
    const { endpoint, keys } = sub.toJSON();
    const r = await send("POST", "/me/push-subscriptions", { endpoint, p256dh: keys?.p256dh, auth: keys?.auth });
    return r.ok ? "on" : "failed";
  } catch {
    return "failed";
  }
}

/** Stops pushes to this browser: unsubscribes it here (what matters) and tells the API (best effort). */
export async function disablePush() {
  const sub = await currentSubscription();
  if (!sub) return;
  await Promise.race([send("POST", "/me/push-subscriptions/remove", { endpoint: sub.endpoint }), new Promise((r) => setTimeout(r, 1500))]);
  await sub.unsubscribe();
}
