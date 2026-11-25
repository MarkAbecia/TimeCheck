"use client";

import { zodResolver } from "@hookform/resolvers/zod";
import { useRouter } from "next/navigation";
import { useTranslations } from "next-intl";
import { useState } from "react";
import { useForm } from "react-hook-form";
import { toast } from "sonner";
import { Field, FormAlert, SelectField } from "@/components/auth/fields";
import { Button } from "@/components/ui/button";
import { send } from "@/lib/api-client";
import type { GroupSummary, LeaderOption } from "@/lib/types";
import { groupSchema, type GroupInput } from "@/lib/validation/member";

function GroupForm({ group, leaders, onDone }: { group?: GroupSummary; leaders: LeaderOption[]; onDone: () => void }) {
  const t = useTranslations("groups");
  const router = useRouter();
  const [alert, setAlert] = useState<string | null>(null);
  const {
    register,
    handleSubmit,
    setError,
    formState: { errors, isSubmitting },
  } = useForm<GroupInput>({
    resolver: zodResolver(groupSchema),
    defaultValues: { name: group?.name ?? "", leaderId: group?.leaderId ?? "" },
  });

  const onSubmit = handleSubmit(async (v) => {
    setAlert(null);
    const body = { name: v.name, leaderId: v.leaderId || null };
    const r = group ? await send("PATCH", `/groups/${group.id}`, body) : await send("POST", "/groups", body);
    if (r.ok) {
      toast.success(t(group ? "saved" : "created", { name: v.name }));
      router.refresh();
      onDone();
      return;
    }
    if (r.problem.status === 409) return setError("name", { message: "groupNameTaken" });
    if (r.problem.status === 400 && r.problem.errors?.leaderId) return setError("leaderId", { message: "leaderInvalid" });
    setAlert(t(r.problem.status === 0 ? "offline" : "unexpected"));
  });

  return (
    <form onSubmit={onSubmit} noValidate className="grid gap-3 sm:grid-cols-[1fr_1fr_auto] sm:items-end">
      {alert && (
        <div className="sm:col-span-3">
          <FormAlert>{alert}</FormAlert>
        </div>
      )}
      <Field label={t("name")} autoComplete="off" registration={register("name")} error={errors.name} />
      <SelectField
        label={t("leader")}
        placeholder={t("noLeader")}
        options={leaders.map((l) => ({ value: l.id, label: l.displayName }))}
        registration={register("leaderId")}
        error={errors.leaderId}
      />
      <div className="flex gap-2">
        <Button type="submit" disabled={isSubmitting}>
          {group ? t("save") : t("create")}
        </Button>
        {group && (
          <Button type="button" variant="ghost" onClick={onDone}>
            {t("cancel")}
          </Button>
        )}
      </div>
    </form>
  );
}

function GroupRow({ group, leaders }: { group: GroupSummary; leaders: LeaderOption[] }) {
  const t = useTranslations("groups");
  const router = useRouter();
  const [editing, setEditing] = useState(false);
  const [confirming, setConfirming] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function archive() {
    setError(null);
    const r = await send("POST", `/groups/${group.id}/archive`);
    setConfirming(false);
    // The group leaves the list once it refreshes, so no toast.
    if (r.ok) router.refresh();
    else setError(t(r.problem.status === 409 ? "hasMembers" : "unexpected"));
  }

  if (editing) return <GroupForm group={group} leaders={leaders} onDone={() => setEditing(false)} />;
  return (
    <div className="grid gap-2">
      <div className="flex flex-wrap items-center gap-x-4 gap-y-2">
        <div className="min-w-0 flex-1">
          <p className="font-semibold">{group.name}</p>
          <p className="text-sm text-muted-foreground">
            {group.leaderName ?? t("noLeader")}, {t("memberCount", { count: group.memberCount })}
          </p>
        </div>
        <Button variant="outline" size="sm" onClick={() => setEditing(true)}>
          {t("edit")}
        </Button>
        {confirming ? (
          <>
            <Button variant="destructive" size="sm" onClick={archive}>
              {t("confirmArchive")}
            </Button>
            <Button variant="ghost" size="sm" onClick={() => setConfirming(false)}>
              {t("cancel")}
            </Button>
          </>
        ) : (
          <Button variant="ghost" size="sm" onClick={() => setConfirming(true)}>
            {t("archive")}
          </Button>
        )}
      </div>
      {error && <FormAlert>{error}</FormAlert>}
    </div>
  );
}

export function GroupsManager({ groups, leaders }: { groups: GroupSummary[]; leaders: LeaderOption[] }) {
  const t = useTranslations("groups");
  return (
    <div className="grid gap-6">
      <section aria-labelledby="new-group" className="grid gap-3 rounded-[10px] border border-border bg-card p-4">
        <h2 id="new-group" className="font-semibold">
          {t("newGroup")}
        </h2>
        <GroupForm key={groups.length} leaders={leaders} onDone={() => {}} />
      </section>
      <ul className="grid divide-y divide-border rounded-[10px] border border-border bg-card">
        {groups.map((g) => (
          <li key={g.id} className="p-4">
            <GroupRow group={g} leaders={leaders} />
          </li>
        ))}
      </ul>
    </div>
  );
}
