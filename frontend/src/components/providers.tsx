import { headers } from "next/headers";
import { NextIntlClientProvider } from "next-intl";
import { ClientProviders } from "./client-providers";

export async function Providers({ children }: { children: React.ReactNode }) {
  const nonce = (await headers()).get("x-nonce") ?? undefined;
  return (
    <NextIntlClientProvider>
      <ClientProviders nonce={nonce}>{children}</ClientProviders>
    </NextIntlClientProvider>
  );
}
