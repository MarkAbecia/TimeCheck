import { getRequestConfig } from "next-intl/server";

// English only for v1; Filipino/Tagalog drops in as messages/fil.json plus a locale source (§11.3).
export default getRequestConfig(async () => {
  const locale = "en";
  return {
    locale,
    timeZone: "Asia/Manila",
    messages: (await import(`../../messages/${locale}.json`)).default,
  };
});
