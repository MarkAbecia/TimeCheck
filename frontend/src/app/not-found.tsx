import Link from "next/link";
import { getTranslations } from "next-intl/server";
import { Compass } from "lucide-react";
import { EmptyState } from "@/components/empty-state";
import { buttonVariants } from "@/components/ui/button";

export default async function NotFound() {
  const t = await getTranslations("notFound");
  return (
    <main className="mx-auto grid min-h-dvh max-w-lg place-items-center px-4">
      <EmptyState
        icon={Compass}
        title={t("title")}
        body={t("body")}
        action={<Link href="/" className={buttonVariants()}>{t("home")}</Link>}
      />
    </main>
  );
}
