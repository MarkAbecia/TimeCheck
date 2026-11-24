"use client";

import { CloudOff } from "lucide-react";
import { useTranslations } from "next-intl";
import { EmptyState } from "@/components/empty-state";
import { Button } from "@/components/ui/button";

export default function AppError({ retry }: { error: unknown; retry: () => void }) {
  const t = useTranslations("today");
  return (
    <div role="alert">
      <EmptyState
        icon={CloudOff}
        title={t("errorTitle")}
        body={t("errorBody")}
        action={<Button onClick={() => retry()}>{t("retry")}</Button>}
      />
    </div>
  );
}
