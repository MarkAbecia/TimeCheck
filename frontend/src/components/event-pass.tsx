import Link from "next/link";
import { Check } from "lucide-react";
import { useTranslations } from "next-intl";
import { cn } from "@/lib/utils";

export type PassStub =
  | { state: "todo"; total: number; href: string } // leader: needs check-in
  | { state: "done"; present: number; total: number; href: string; justMarked?: boolean } // leader: marked, still editable
  | { state: "upcoming"; day: string; time: string }
  | { state: "attended"; justMarked?: boolean } // member: marked present
  | { state: "pending" }; // member: not marked yet

export type Pass = {
  id: string;
  kind: "group" | "church";
  timeLabel: string;
  name: string;
  meta: string;
  stub: PassStub;
};

/**
 * The Event Pass: a ticket with a stub. The stub carries the state and the action; the row never restyles (direction contract).
 */
export function EventPass({ pass }: { pass: Pass }) {
  const t = useTranslations("pass");
  return (
    <article
      aria-labelledby={`pass-${pass.id}`}
      className="relative grid min-h-24 grid-cols-[1fr_6rem] overflow-hidden rounded-[10px] border border-border bg-card"
    >
      <div className={cn("min-w-0 border-t-[6px] px-3.5 pt-3 pb-3.5", pass.kind === "group" ? "border-band-group" : "border-band-church")}>
        <span className="sr-only">{pass.kind === "group" ? t("groupEvent") : t("churchEvent")}. </span>
        <p className="tabular text-[0.8125rem] font-semibold text-muted-foreground">{pass.timeLabel}</p>
        <h3 id={`pass-${pass.id}`} className="mt-0.5 font-display text-2xl leading-[1.05] font-bold text-balance uppercase">
          {pass.name}
        </h3>
        <p className="mt-1 text-[0.8125rem] text-muted-foreground">{pass.meta}</p>
      </div>
      <Stub stub={pass.stub} eventName={pass.name} />
      {/* Perforation notches, punched in the page colour where the stub meets the pass edges. */}
      <span
        aria-hidden
        className="pointer-events-none absolute -top-2.5 right-[calc(6rem-0.625rem)] size-5 rounded-full border border-border bg-background"
      />
      <span
        aria-hidden
        className="pointer-events-none absolute -bottom-2.5 right-[calc(6rem-0.625rem)] size-5 rounded-full border border-border bg-background"
      />
    </article>
  );
}

const fillPct = (present: number, total: number) => (total > 0 ? Math.round((present / total) * 100) : 0);

const stubBase = "flex flex-col items-center justify-center border-l-2 border-dashed border-border px-2 py-3 text-center";

function Stub({ stub, eventName }: { stub: PassStub; eventName: string }) {
  const t = useTranslations("pass");

  switch (stub.state) {
    case "todo":
      return (
        <Link
          href={stub.href}
          className={cn(
            stubBase,
            "border-l-primary/40 bg-primary text-primary-foreground transition-[background-color] duration-150 hover:bg-[color-mix(in_oklch,var(--primary),black_12%)] active:bg-[color-mix(in_oklch,var(--primary),black_20%)]",
          )}
        >
          <span className="font-display text-[0.9375rem] leading-tight font-bold tracking-[0.06em] uppercase">{t("checkIn")}</span>{" "}
          <span className="tabular mt-0.5 text-xs">{t("ofTotal", { present: 0, total: stub.total })}</span>{" "}
          {/* Name = the visible words first (WCAG 2.5.3), then which event. */}
          <span className="sr-only">{t("forEvent", { event: eventName })}</span>
        </Link>
      );
    case "done":
      return (
        <Link
          href={stub.href}
          className={cn(stubBase, "transition-[filter] hover:brightness-95 dark:hover:brightness-110")}
          // The one fixed ramp: the stub fills from the bottom with the share checked in.
          style={{ background: `linear-gradient(to top, var(--stub-fill) ${fillPct(stub.present, stub.total)}%, var(--surface-2) 0)` }}
        >
          <span className="tabular font-display text-[1.875rem] leading-none font-bold">
            {stub.present}/{stub.total}
          </span>
          <span className="mt-1 text-xs text-muted-foreground">{t("checkedIn")}</span>
          <span className="mt-1 inline-flex items-center gap-1 text-xs font-semibold">
            <Check className={cn("size-3.5 text-success", stub.justMarked && "animate-stamp")} strokeWidth={3} aria-hidden />
            {t("marked")}
          </span>
        </Link>
      );
    case "upcoming":
      return (
        <div className={stubBase}>
          <span className="text-xs text-muted-foreground">{t("opens")}</span>
          <span className="mt-0.5 font-display text-[1.625rem] leading-none font-bold uppercase">{stub.day}</span>
          <span className="tabular mt-0.5 font-display text-[0.9375rem] font-semibold uppercase">{stub.time}</span>
        </div>
      );
    case "attended":
      return (
        <div className={cn(stubBase, "bg-surface-2")}>
          <Check className={cn("size-7 text-success", stub.justMarked && "animate-stamp")} strokeWidth={3} aria-hidden />
          <span className="mt-1 text-xs font-semibold">{t("attended")}</span>
        </div>
      );
    case "pending":
      return (
        <div className={stubBase}>
          <span className="text-xs text-muted-foreground">{t("notMarked")}</span>
        </div>
      );
  }
}

export function EventPassSkeleton() {
  return (
    <div aria-hidden className="grid min-h-24 grid-cols-[1fr_6rem] overflow-hidden rounded-[10px] border border-border bg-card">
      <div className="border-t-[6px] border-muted px-3.5 pt-3 pb-3.5">
        <div className="h-3 w-24 animate-pulse rounded bg-muted" />
        <div className="mt-2 h-6 w-40 animate-pulse rounded bg-muted" />
        <div className="mt-2 h-3 w-32 animate-pulse rounded bg-muted" />
      </div>
      <div className="border-l-2 border-dashed border-border bg-surface-2/60" />
    </div>
  );
}
