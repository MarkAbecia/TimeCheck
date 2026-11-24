// The church's timezone (§5.2). The API stores UTC; everything people see and type is Manila local time.
export const TZ = "Asia/Manila";
// shortcut: Manila has no DST, so local wall time is always UTC+8. Read the offset per date if a church elsewhere joins (§15 Q8).
const OFFSET = "+08:00";

const parts = (d: Date, o: Intl.DateTimeFormatOptions) => new Intl.DateTimeFormat("en-PH", { timeZone: TZ, ...o }).format(d);

/** yyyy-mm-dd of an instant in church time. */
export const localDate = (d: Date) => new Intl.DateTimeFormat("en-CA", { timeZone: TZ }).format(d);
/** HH:mm (24h) of an instant in church time, for time inputs. */
export const localTime = (d: Date) =>
  new Intl.DateTimeFormat("en-GB", { timeZone: TZ, hour: "2-digit", minute: "2-digit", hourCycle: "h23" }).format(d);
/** A church-time date and time as an ISO instant. */
export const toInstant = (date: string, time: string) => `${date}T${time}:00${OFFSET}`;

export const addDays = (date: string, days: number) => {
  const d = new Date(`${date}T00:00:00Z`);
  d.setUTCDate(d.getUTCDate() + days);
  return d.toISOString().slice(0, 10);
};

/** The Monday on or before a date (weeks start Monday; Sunday ends them, after Cell Celebration). */
export const weekStart = (date: string) => {
  const dow = new Date(`${date}T00:00:00Z`).getUTCDay();
  return addDays(date, dow === 0 ? -6 : 1 - dow);
};

/** "5:00 – 6:00 AM", "7:30 – 9:30 PM", "11:00 AM – 1:00 PM". */
export function timeRange(startsAt: string, endsAt: string) {
  const s = new Date(startsAt);
  const e = new Date(endsAt);
  const ampm = (d: Date) => parts(d, { hour: "numeric", hour12: true }).slice(-2).toUpperCase();
  const hm = (d: Date) => parts(d, { hour: "numeric", minute: "2-digit", hour12: true }).replace(/\s?[AP]M$/i, "");
  return ampm(s) === ampm(e) ? `${hm(s)} – ${hm(e)} ${ampm(e)}` : `${hm(s)} ${ampm(s)} – ${hm(e)} ${ampm(e)}`;
}

/** "Tue 24 Nov": day before month, as Filipinos write it. */
export const dayLabel = (d: Date) => `${parts(d, { weekday: "short" })} ${parts(d, { day: "numeric" })} ${parts(d, { month: "short" })}`;
export const weekday = (d: Date) => parts(d, { weekday: "short" });
/** "7 PM", "5:30 AM". */
export const shortTime = (d: Date) => parts(d, { hour: "numeric", minute: "2-digit", hour12: true }).replace(":00", "").toUpperCase();
