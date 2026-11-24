"use client";

import { zodResolver } from "@hookform/resolvers/zod";
import { useRouter } from "next/navigation";
import { useTranslations } from "next-intl";
import { useState } from "react";
import { useForm, useWatch, type Path } from "react-hook-form";
import { toast } from "sonner";
import { CheckField, Field, FormAlert, SelectField, SubmitButton } from "@/components/auth/fields";
import { Button } from "@/components/ui/button";
import { send, type Problem } from "@/lib/api-client";
import type { Member } from "@/lib/types";
import { editSchema, isMinor, registerSchema, toMemberBody, type RegisterInput } from "@/lib/validation/member";

/** `guest`: registering someone who came as a guest (§6.5); the invite is linked to the new member. */
type Props = { mode: "register"; groupId: string; guest?: { inviteId: string; name: string } } | { mode: "edit"; member: Member };
type Values = RegisterInput; // edit uses the same fields; email is ignored there

const FIELDS = [
  "firstName",
  "lastName",
  "nickname",
  "gender",
  "birthdate",
  "gradeLevel",
  "mobile",
  "email",
  "address",
  "joinedOn",
] as const;

function defaults(props: Props): Values {
  if (props.mode === "register") {
    // A guest's name is one free-text field; the last word is a fair first guess at the surname.
    const words = props.guest?.name.trim().split(/\s+/) ?? [];
    return {
      firstName: words.length > 1 ? words.slice(0, -1).join(" ") : (words[0] ?? ""),
      lastName: words.length > 1 ? words[words.length - 1] : "",
      nickname: "",
      gender: "" as Values["gender"],
      birthdate: "",
      gradeLevel: "",
      mobile: "",
      email: "",
      address: "",
      joinedOn: "",
      guardianName: "",
      guardianContact: "",
      guardianConsent: false,
    };
  }
  const m = props.member;
  return {
    firstName: m.firstName,
    lastName: m.lastName,
    nickname: m.nickname ?? "",
    gender: m.gender,
    birthdate: m.birthdate,
    gradeLevel: m.gradeLevel ?? "",
    mobile: m.mobile ?? "",
    email: m.email,
    address: m.address ?? "",
    joinedOn: m.joinedOn,
    guardianName: m.guardianName ?? "",
    guardianContact: m.guardianContact ?? "",
    guardianConsent: m.guardianConsentAt !== null,
  };
}

export function MemberForm(props: Props) {
  const t = useTranslations("members");
  const router = useRouter();
  const [alert, setAlert] = useState<string | null>(null);
  const [duplicates, setDuplicates] = useState<Problem["matches"] | null>(null);
  const editing = props.mode === "edit";
  const {
    register,
    handleSubmit,
    control,
    setError,
    formState: { errors, isSubmitting },
  } = useForm<Values>({
    resolver: zodResolver((editing ? editSchema : registerSchema) as typeof registerSchema),
    mode: "onTouched",
    defaultValues: defaults(props),
  });
  const minor = isMinor(useWatch({ control, name: "birthdate" }) ?? "");

  const save = (confirmDuplicates: boolean) =>
    handleSubmit(async (values) => {
      setAlert(null);
      const body = toMemberBody(values);
      const r =
        props.mode === "register"
          ? await send<Member>("POST", `/groups/${props.groupId}/members`, {
              ...body,
              confirmDuplicates,
              inviteId: props.guest?.inviteId ?? null,
            })
          : await send<Member>("PATCH", `/members/${props.member.id}`, body);
      if (r.ok) {
        toast.success(t(editing ? "saved" : "registered", { name: r.data.displayName }));
        if (editing) router.refresh();
        else router.replace(`/members/${r.data.id}`);
        return;
      }
      const p = r.problem;
      if (p.code === "duplicate.warning") return setDuplicates(p.matches ?? []);
      setDuplicates(null);
      if (p.status === 409 && !editing) return setError("email", { message: "emailTaken" });
      if (p.status === 400 && p.errors) {
        // API field names are camelCase; guardian errors arrive as guardian.name etc.
        let placed = false;
        for (const key of Object.keys(p.errors)) {
          const field = (
            key.startsWith("guardian") ? `guardian${key.split(".")[1]?.replace(/^./, (c) => c.toUpperCase()) ?? "Name"}` : key
          ) as Path<Values>;
          if ([...FIELDS, "guardianName", "guardianContact", "guardianConsent"].includes(field)) {
            setError(field, { message: "checkField" });
            placed = true;
          }
        }
        if (placed) return;
      }
      setAlert(p.status === 0 ? t("offline") : p.status === 422 && p.title ? p.title : t("unexpected"));
    });

  const genders = [
    { value: "FEMALE", label: t("female") },
    { value: "MALE", label: t("male") },
  ];

  return (
    <form onSubmit={save(false)} noValidate className="grid gap-5">
      {alert && <FormAlert>{alert}</FormAlert>}

      <fieldset className="grid gap-4 sm:grid-cols-2">
        <legend className="sr-only">{t("nameLegend")}</legend>
        <Field label={t("firstName")} autoComplete="off" registration={register("firstName")} error={errors.firstName} />
        <Field label={t("lastName")} autoComplete="off" registration={register("lastName")} error={errors.lastName} />
        <Field label={t("nickname")} autoComplete="off" registration={register("nickname")} error={errors.nickname} />
        <SelectField
          label={t("gender")}
          placeholder={t("choose")}
          options={genders}
          registration={register("gender")}
          error={errors.gender}
        />
        <Field label={t("birthdate")} type="date" registration={register("birthdate")} error={errors.birthdate} />
        <Field label={t("gradeLevel")} autoComplete="off" registration={register("gradeLevel")} error={errors.gradeLevel} />
      </fieldset>

      <fieldset className="grid gap-4 sm:grid-cols-2">
        <legend className="sr-only">{t("contactLegend")}</legend>
        {!editing && (
          <Field
            label={t("email")}
            type="email"
            inputMode="email"
            autoCapitalize="none"
            spellCheck={false}
            autoComplete="off"
            registration={register("email")}
            error={errors.email}
          />
        )}
        <Field label={t("mobile")} type="tel" inputMode="tel" autoComplete="off" registration={register("mobile")} error={errors.mobile} />
        <div className="sm:col-span-2">
          <Field label={t("address")} autoComplete="off" registration={register("address")} error={errors.address} />
        </div>
        <Field
          label={editing ? t("joinedOn") : t("joinedOnOptional")}
          type="date"
          registration={register("joinedOn")}
          error={errors.joinedOn}
        />
      </fieldset>

      {minor && (
        <fieldset className="grid gap-4 rounded-[10px] border border-border bg-muted/40 p-4 sm:grid-cols-2">
          <legend className="px-1 font-semibold">{t("guardianLegend")}</legend>
          <p className="text-sm text-muted-foreground sm:col-span-2">{t("guardianLead")}</p>
          <Field label={t("guardianName")} autoComplete="off" registration={register("guardianName")} error={errors.guardianName} />
          <Field
            label={t("guardianContact")}
            autoComplete="off"
            registration={register("guardianContact")}
            error={errors.guardianContact}
          />
          <div className="sm:col-span-2">
            <CheckField label={t("guardianConsent")} registration={register("guardianConsent")} error={errors.guardianConsent} />
          </div>
        </fieldset>
      )}

      {duplicates && (
        <div role="alert" className="grid gap-3 rounded-[10px] border border-warning/40 bg-warning/10 p-4 text-sm">
          <p className="font-semibold">{t("duplicateTitle")}</p>
          <ul className="list-disc pl-5">
            {duplicates.map((d, i) => (
              <li key={i}>
                {t(d.field === "mobile" ? "duplicateMobile" : "duplicateName", { where: t(d.sameGroup ? "sameGroup" : "otherGroup") })}
              </li>
            ))}
          </ul>
          <p className="text-muted-foreground">{t("duplicateLead")}</p>
          <div className="flex flex-wrap gap-2">
            <Button type="button" variant="outline" onClick={save(true)} disabled={isSubmitting}>
              {t("registerAnyway")}
            </Button>
            <Button type="button" variant="ghost" onClick={() => setDuplicates(null)}>
              {t("checkAgain")}
            </Button>
          </div>
        </div>
      )}

      {!duplicates && (
        <SubmitButton
          pending={isSubmitting}
          label={editing ? t("save") : t("register")}
          pendingLabel={editing ? t("saving") : t("registering")}
        />
      )}
    </form>
  );
}
