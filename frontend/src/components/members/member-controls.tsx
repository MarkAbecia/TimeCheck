"use client";

import { useRouter } from "next/navigation";
import { useTranslations } from "next-intl";
import { useId, useState } from "react";
import { toast } from "sonner";
import { FormAlert } from "@/components/auth/fields";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { send, type Problem } from "@/lib/api-client";
import { MEMBER_STATUSES, type ApiRole, type GroupSummary, type Member } from "@/lib/types";

const selectClass =
  "h-11 w-full min-w-0 rounded-lg border border-input bg-card px-3 text-base outline-none focus-visible:border-ring focus-visible:ring-3 focus-visible:ring-ring/50 dark:bg-input/30";

/** One labelled select + optional reason + Save, posting `{ [field]: value, reason }` to the member's sub-resource. */
function Control({
  member,
  title,
  field,
  path,
  options,
  initial,
  withReason,
}: {
  member: Member;
  title: string;
  field: "status" | "role" | "groupId";
  path: "status" | "role" | "move";
  options: { value: string; label: string }[];
  initial: string;
  withReason?: boolean;
}) {
  const t = useTranslations("members");
  const router = useRouter();
  const id = useId();
  const [value, setValue] = useState(initial);
  const [reason, setReason] = useState("");
  const [pending, setPending] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function save(e: React.FormEvent) {
    e.preventDefault();
    setPending(true);
    setError(null);
    const r = await send<Member>("POST", `/members/${member.id}/${path}`, {
      [field]: value,
      ...(withReason ? { reason: reason.trim() || null } : {}),
    });
    setPending(false);
    if (r.ok) {
      setReason("");
      toast.success(t("saved", { name: r.data.displayName }));
      router.refresh();
    } else setError(problemText(r.problem, t));
  }

  return (
    <form onSubmit={save} className="grid gap-3 rounded-[10px] border border-border bg-card p-4">
      {error && <FormAlert>{error}</FormAlert>}
      <div className="grid gap-1.5">
        <Label htmlFor={`${id}-v`} className="font-semibold">
          {title}
        </Label>
        <select id={`${id}-v`} className={selectClass} value={value} onChange={(e) => setValue(e.target.value)}>
          {options.map((o) => (
            <option key={o.value} value={o.value}>
              {o.label}
            </option>
          ))}
        </select>
      </div>
      {withReason && (
        <div className="grid gap-1.5">
          <Label htmlFor={`${id}-r`}>{t("reason")}</Label>
          <Input id={`${id}-r`} maxLength={500} value={reason} onChange={(e) => setReason(e.target.value)} />
        </div>
      )}
      <div>
        <Button type="submit" variant="outline" disabled={pending || value === initial}>
          {pending ? t("saving") : t("save")}
        </Button>
      </div>
    </form>
  );
}

function problemText(p: Problem, t: ReturnType<typeof useTranslations>) {
  if (p.status === 0) return t("offline");
  if ((p.status === 409 || p.status === 422) && p.title) return p.title; // the API explains conflicts in words (e.g. still leads a group)
  if (p.status === 403) return t("needsMfa");
  return t("unexpected");
}

export function MemberControls({
  member,
  isAdmin,
  isSuper,
  groups,
}: {
  member: Member;
  isAdmin: boolean;
  isSuper: boolean;
  groups: GroupSummary[];
}) {
  const t = useTranslations("members");
  const tr = useTranslations("roles");
  const roles: ApiRole[] = isSuper ? ["MEMBER", "LEADER", "ADMIN", "SUPER_ADMIN"] : ["MEMBER", "LEADER"];
  return (
    <div className="grid gap-4 md:grid-cols-2">
      <Control
        member={member}
        title={t("statusLabel")}
        field="status"
        path="status"
        initial={member.status}
        withReason
        options={MEMBER_STATUSES.map((s) => ({ value: s, label: t(`status.${s}`) }))}
      />
      {isAdmin && (
        <Control
          member={member}
          title={t("roleLabel")}
          field="role"
          path="role"
          initial={member.role}
          withReason
          options={roles.map((r) => ({ value: r, label: tr(r.toLowerCase()) }))}
        />
      )}
      {isAdmin && groups.length > 0 && (
        <Control
          member={member}
          title={t("groupLabel")}
          field="groupId"
          path="move"
          initial={member.groupId ?? ""}
          options={[
            ...(member.groupId ? [] : [{ value: "", label: t("noGroup") }]),
            ...groups.map((g) => ({ value: g.id, label: g.name })),
          ]}
        />
      )}
    </div>
  );
}
