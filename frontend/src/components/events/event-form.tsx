"use client";

import { zodResolver } from "@hookform/resolvers/zod";
import { useRouter } from "next/navigation";
import { useTranslations } from "next-intl";
import { useState } from "react";
import { useForm, useWatch } from "react-hook-form";
import { toast } from "sonner";
import { CheckField, Field, FormAlert, SelectField, SubmitButton } from "@/components/auth/fields";
import { Button } from "@/components/ui/button";
import { Label } from "@/components/ui/label";
import { send, type Problem } from "@/lib/api-client";
import { localDate, localTime, timeRange, dayLabel } from "@/lib/time";
import type { EventDetail, EventType, GroupSummary } from "@/lib/types";
import { eventSchema, toEventBody, type EventInput, type EventValues } from "@/lib/validation/event";

type Props = {
  types: EventType[];
  groups: GroupSummary[];
  isAdmin: boolean;
  event?: EventDetail; // edit mode
  defaultGroupId?: string;
};

function defaults({ types, groups, event, defaultGroupId }: Props): EventInput {
  if (event) {
    const s = new Date(event.startsAt);
    const e = new Date(event.endsAt);
    return {
      eventTypeId: event.eventTypeId,
      groupId: event.groupId ?? "",
      title: event.title,
      date: localDate(s),
      start: localTime(s),
      end: localTime(e),
      endsNextDay: localDate(e) !== localDate(s),
      location: event.location ?? "",
      onlineLink: event.onlineLink ?? "",
      description: event.description ?? "",
      repeatWeeks: 1,
      audienceGroupIds: event.audienceGroupIds,
    };
  }
  const cell = types.find((t) => t.code === "CELL_MEETING") ?? types[0];
  return {
    eventTypeId: cell?.id ?? "",
    groupId: defaultGroupId ?? groups[0]?.id ?? "",
    title: "",
    date: localDate(new Date()),
    start: "19:00",
    end: "21:00",
    endsNextDay: false,
    location: "",
    onlineLink: "",
    description: "",
    repeatWeeks: 1,
    audienceGroupIds: [],
  };
}

export function EventForm(props: Props) {
  const { types, groups, isAdmin, event } = props;
  const t = useTranslations("events");
  const router = useRouter();
  const [alert, setAlert] = useState<string | null>(null);
  const [conflicts, setConflicts] = useState<Problem["conflicts"] | null>(null);
  const {
    register,
    handleSubmit,
    control,
    setError,
    formState: { errors, isSubmitting },
  } = useForm<EventInput, unknown, EventValues>({ resolver: zodResolver(eventSchema), mode: "onTouched", defaultValues: defaults(props) });

  const typeId = useWatch({ control, name: "eventTypeId" });
  const groupId = useWatch({ control, name: "groupId" });
  const type = types.find((x) => x.id === typeId);
  const groupScoped = type?.defaultScope === "GROUP";
  const isCell = type?.code === "CELL_MEETING";

  const save = (confirmConflicts: boolean) =>
    handleSubmit(async (v) => {
      setAlert(null);
      const body = toEventBody(v);
      const r = event
        ? await send<EventDetail>("PATCH", `/events/${event.id}`, { ...body, confirmConflicts })
        : await send<EventDetail>("POST", "/events", { ...body, confirmConflicts });
      if (r.ok) {
        toast.success(t(event ? "saved" : "scheduled", { title: r.data.title }));
        router.replace(`/events/${r.data.id}`);
        router.refresh();
        return;
      }
      const p = r.problem;
      if (p.code === "event.conflict") {
        setConflicts(p.conflicts ?? []);
        // The warning lands below the fold on a phone; bring it up so the choice is seen.
        requestAnimationFrame(() => document.getElementById("event-conflicts")?.scrollIntoView({ block: "center" }));
        return;
      }
      setConflicts(null);
      if (p.status === 400 && p.errors) {
        for (const key of Object.keys(p.errors)) {
          if (key === "groupId") setError("groupId", { message: "required" });
          else if (key === "endsAt") setError("end", { message: "endBeforeStart" });
          else if (key === "onlineLink") setError("onlineLink", { message: "linkInvalid" });
        }
        return;
      }
      setAlert(p.status === 0 ? t("offline") : (p.status === 403 || p.status === 422) && p.title ? p.title : t("unexpected"));
    });

  // Leaders schedule only for groups they lead; admins may also choose the whole church for church-wide types.
  const groupOptions = [
    ...(isAdmin && !groupScoped ? [{ value: "", label: t("wholeChurch") }] : []),
    ...groups.map((g) => ({ value: g.id, label: g.name })),
  ];
  const typeOptions = types.filter((x) => x.schedulable && (isAdmin || x.leadersMaySchedule)).map((x) => ({ value: x.id, label: x.name }));

  return (
    <form onSubmit={save(false)} noValidate className="grid gap-5">
      {alert && <FormAlert>{alert}</FormAlert>}
      <div className="grid gap-4 sm:grid-cols-2">
        {event ? (
          // The type and group are fixed once scheduled.
          <p className="text-muted-foreground sm:col-span-2">
            {event.typeName}, {event.groupName ?? t("wholeChurch")}
          </p>
        ) : (
          <>
            <SelectField label={t("type")} options={typeOptions} registration={register("eventTypeId")} error={errors.eventTypeId} />
            <SelectField label={t("group")} options={groupOptions} registration={register("groupId")} error={errors.groupId} />
          </>
        )}
        <div className="sm:col-span-2">
          <Field
            label={t("titleField")}
            placeholder={type?.name}
            autoComplete="off"
            registration={register("title")}
            error={errors.title}
          />
        </div>
        <Field label={t("date")} type="date" registration={register("date")} error={errors.date} />
        <div className="grid grid-cols-2 gap-3">
          <Field label={t("start")} type="time" registration={register("start")} error={errors.start} />
          <Field label={t("end")} type="time" registration={register("end")} error={errors.end} />
        </div>
        <div className="sm:col-span-2">
          <CheckField label={t("endsNextDay")} registration={register("endsNextDay")} />
        </div>
        <Field label={t("location")} autoComplete="off" registration={register("location")} error={errors.location} />
        <Field
          label={t("onlineLink")}
          type="url"
          inputMode="url"
          placeholder="https://"
          autoComplete="off"
          registration={register("onlineLink")}
          error={errors.onlineLink}
        />
        <div className="grid gap-1.5 sm:col-span-2">
          <Label htmlFor="event-description">{t("description")}</Label>
          <textarea
            id="event-description"
            rows={3}
            maxLength={4000}
            className="w-full min-w-0 rounded-lg border border-input bg-card px-3 py-2 text-base outline-none focus-visible:border-ring focus-visible:ring-3 focus-visible:ring-ring/50 dark:bg-input/30"
            {...register("description")}
          />
        </div>
        {isCell && !event && (
          <Field
            label={t("repeatWeeks")}
            type="number"
            inputMode="numeric"
            registration={register("repeatWeeks", { min: 1, max: 26 })}
            error={errors.repeatWeeks}
          />
        )}
      </div>

      {isAdmin && !groupScoped && groupId === "" && groups.length > 0 && !event && (
        <fieldset className="grid gap-2 rounded-[10px] border border-border p-4">
          <legend className="px-1 font-semibold">{t("audience")}</legend>
          <p className="text-sm text-muted-foreground">{t("audienceLead")}</p>
          <div className="grid gap-1 sm:grid-cols-2">
            {groups.map((g) => (
              <label key={g.id} className="flex min-h-11 items-center gap-3 text-sm">
                <input type="checkbox" value={g.id} className="size-5 accent-primary" {...register("audienceGroupIds")} />
                {g.name}
              </label>
            ))}
          </div>
        </fieldset>
      )}

      {conflicts && (
        <div id="event-conflicts" role="alert" className="grid gap-3 rounded-[10px] border border-warning/40 bg-warning/10 p-4 text-sm">
          <p className="font-semibold">{t("conflictTitle")}</p>
          <ul className="list-disc pl-5">
            {conflicts.map((c) => (
              <li key={c.id}>
                {c.title}, {dayLabel(new Date(c.startsAt))} {timeRange(c.startsAt, c.endsAt)}
              </li>
            ))}
          </ul>
          <div className="flex flex-wrap gap-2">
            <Button type="button" variant="outline" onClick={save(true)} disabled={isSubmitting}>
              {t("scheduleAnyway")}
            </Button>
            <Button type="button" variant="ghost" onClick={() => setConflicts(null)}>
              {t("changeTime")}
            </Button>
          </div>
        </div>
      )}

      {!conflicts && <SubmitButton pending={isSubmitting} label={event ? t("save") : t("schedule")} pendingLabel={t("saving")} />}
    </form>
  );
}
