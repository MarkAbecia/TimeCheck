import Link from "next/link";
import { getTranslations } from "next-intl/server";
import { Hammer } from "lucide-react";
import { EmptyState } from "@/components/empty-state";
import { buttonVariants } from "@/components/ui/button";

// Every nav destination resolves to this until its phase ships, so the shell never links to a 404.
export default async function SectionPlaceholder() {
  const t = await getTranslations("placeholder");
  return (
    <EmptyState
      icon={Hammer}
      title={t("title")}
      body={t("body")}
      action={
        <Link href="/" className={buttonVariants({ variant: "outline" })}>
          {t("back")}
        </Link>
      }
    />
  );
}
