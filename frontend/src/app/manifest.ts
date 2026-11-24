import type { MetadataRoute } from "next";

// Lets people add TimeCheck to their home screen; on iPhone that's also what makes web push possible (§6.7).
// shortcut: no app icons yet (the browser uses a screenshot); add 192/512 px icons with the brand mark in Phase 11.
export default function manifest(): MetadataRoute.Manifest {
  return {
    name: "TimeCheck",
    short_name: "TimeCheck",
    start_url: "/",
    display: "standalone",
    background_color: "#0b0b0c",
    theme_color: "#0b0b0c",
  };
}
