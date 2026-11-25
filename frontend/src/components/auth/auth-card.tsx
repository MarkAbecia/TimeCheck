/** A pass-shaped card: white stock, red band on top. */
export function AuthCard({ title, lead, children }: { title: string; lead?: string; children: React.ReactNode }) {
  return (
    <div className="mx-auto w-full max-w-[400px] overflow-hidden rounded-[10px] border border-border bg-card shadow-card">
      <div className="border-t-[6px] border-band-group px-5 pt-5 pb-6 sm:px-7 sm:pt-6 sm:pb-7">
        <h1 className="font-display text-[2rem] leading-none font-bold uppercase">{title}</h1>
        {lead && <p className="mt-2 text-pretty text-muted-foreground">{lead}</p>}
        <div className="mt-6">{children}</div>
      </div>
    </div>
  );
}
