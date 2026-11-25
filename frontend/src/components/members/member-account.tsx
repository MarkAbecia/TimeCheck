"use client";

import { Download } from "lucide-react";
import { useRouter } from "next/navigation";
import { useTranslations } from "next-intl";
import { useId, useState } from "react";
import { toast } from "sonner";
import { FormAlert } from "@/components/auth/fields";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { send, type Problem } from "@/lib/api-client";
import type { Member } from "@/lib/types";

/** Admins only (§6.10, §8.4): deactivate or reactivate, download the person's data, and erase a deactivated person. */
export function MemberAccount({ member }: { member: Member }) {
  const t = useTranslations("members.account");
  const router = useRouter();
  const id = useId();
  const [pending, setPending] = useState<"active" | "export" | "erase" | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [confirmDeactivate, setConfirmDeactivate] = useState(false);
  const [typed, setTyped] = useState("");

  const problem = (p: Problem) =>
    p.status === 0 ? t("offline") : p.status === 403 ? t("needsMfa") : p.status === 422 && p.title ? p.title : t("unexpected");

  async function setActive(active: boolean) {
    setPending("active");
    setError(null);
    const r = await send<Member>("POST", `/members/${member.id}/active`, { active });
    setPending(null);
    setConfirmDeactivate(false);
    if (!r.ok) return setError(problem(r.problem));
    toast.success(active ? t("reactivated", { name: r.data.displayName }) : t("deactivated", { name: r.data.displayName }));
    router.refresh();
  }

  async function exportData() {
    setPending("export");
    setError(null);
    const r = await send<unknown>("GET", `/members/${member.id}/data-export`);
    setPending(null);
    if (!r.ok) return setError(problem(r.problem));
    const url = URL.createObjectURL(new Blob([JSON.stringify(r.data, null, 2)], { type: "application/json" }));
    const a = Object.assign(document.createElement("a"), { href: url, download: `timecheck-data-${member.id}.json` });
    a.click();
    URL.revokeObjectURL(url);
  }

  async function erase(e: React.FormEvent) {
    e.preventDefault();
    setPending("erase");
    setError(null);
    const r = await send<null>("POST", `/members/${member.id}/erase`);
    setPending(null);
    if (!r.ok) return setError(problem(r.problem));
    toast.success(t("erased"));
    router.push(member.groupId ? `/members?group=${member.groupId}` : "/members");
  }

  return (
    <div className="grid gap-4 rounded-[10px] border border-border bg-card p-4">
      {error && <FormAlert>{error}</FormAlert>}

      <div className="grid gap-2">
        <p>{member.isActive ? t("activeNow") : t("inactiveNow")}</p>
        {member.isActive && !confirmDeactivate && (
          <Button type="button" variant="outline" className="w-fit" onClick={() => setConfirmDeactivate(true)}>
            {t("deactivate")}
          </Button>
        )}
        {member.isActive && confirmDeactivate && (
          <div className="grid gap-2 rounded-lg border border-border bg-muted/40 p-3">
            <p className="text-sm">{t("deactivateConfirm", { name: member.displayName })}</p>
            <div className="flex flex-wrap gap-2">
              <Button type="button" variant="destructive" disabled={pending !== null} onClick={() => setActive(false)}>
                {pending === "active" ? t("working") : t("deactivateYes")}
              </Button>
              <Button type="button" variant="ghost" onClick={() => setConfirmDeactivate(false)}>
                {t("cancel")}
              </Button>
            </div>
          </div>
        )}
        {!member.isActive && (
          <Button type="button" variant="outline" className="w-fit" disabled={pending !== null} onClick={() => setActive(true)}>
            {pending === "active" ? t("working") : t("reactivate")}
          </Button>
        )}
      </div>

      <div className="grid gap-2 border-t border-border pt-4">
        <p className="text-sm text-muted-foreground">{t("exportLead")}</p>
        <Button type="button" variant="outline" className="w-fit" disabled={pending !== null} onClick={exportData}>
          <Download className="size-4" aria-hidden />
          {pending === "export" ? t("working") : t("export")}
        </Button>
      </div>

      {!member.isActive && (
        <form onSubmit={erase} className="grid gap-2 border-t border-border pt-4">
          <p className="font-semibold">{t("eraseTitle")}</p>
          <p className="text-sm text-muted-foreground">{t("eraseLead")}</p>
          <div className="grid gap-1.5">
            <Label htmlFor={`${id}-confirm`}>{t("eraseType", { name: member.displayName })}</Label>
            <Input id={`${id}-confirm`} autoComplete="off" value={typed} onChange={(e) => setTyped(e.target.value)} />
          </div>
          <Button type="submit" variant="destructive" className="w-fit" disabled={pending !== null || typed.trim() !== member.displayName}>
            {pending === "erase" ? t("working") : t("erase")}
          </Button>
        </form>
      )}
    </div>
  );
}
