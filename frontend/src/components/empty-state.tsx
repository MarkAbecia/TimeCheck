import type { LucideIcon } from "lucide-react";

export function EmptyState({
  icon: Icon,
  title,
  body,
  action,
}: {
  icon: LucideIcon;
  title: string;
  body: string;
  action?: React.ReactNode;
}) {
  return (
    <div className="flex flex-col items-center rounded-[10px] border border-dashed border-border bg-card px-6 py-12 text-center">
      <Icon className="size-8 text-muted-foreground" aria-hidden />
      <h2 className="mt-3 font-display text-2xl font-bold uppercase">{title}</h2>
      <p className="mt-1 max-w-sm text-pretty text-muted-foreground">{body}</p>
      {action && <div className="mt-5">{action}</div>}
    </div>
  );
}
