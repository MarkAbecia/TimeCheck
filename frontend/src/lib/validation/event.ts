import { z } from "zod";
import { toInstant } from "../time";

// Mirrors the API (Features/Events/Scheduling.cs). Messages are keys into messages → "errors".
const DATE = /^\d{4}-\d{2}-\d{2}$/;
const TIME = /^\d{2}:\d{2}$/;

export const eventSchema = z
  .object({
    eventTypeId: z.string().min(1, "required"),
    groupId: z.string(), // "" = whole church
    title: z.string().trim().max(200, "tooLong"),
    date: z.string().regex(DATE, "dateRequired"),
    start: z.string().regex(TIME, "timeRequired"),
    end: z.string().regex(TIME, "timeRequired"),
    endsNextDay: z.boolean(),
    location: z.string().trim().max(300, "tooLong"),
    onlineLink: z
      .string()
      .trim()
      .max(2000, "tooLong")
      .refine((v) => v === "" || /^https:\/\/\S+$/.test(v), "linkInvalid"),
    description: z.string().trim().max(4000, "tooLong"),
    repeatWeeks: z.coerce.number<string | number>().int("checkField").min(1, "checkField").max(26, "repeatTooMany"),
    audienceGroupIds: z.array(z.string()),
  })
  .refine((v) => v.endsNextDay || v.end > v.start, { message: "endBeforeStart", path: ["end"] });

export type EventInput = z.input<typeof eventSchema>;
export type EventValues = z.output<typeof eventSchema>;

const blank = (s: string) => (s.trim() === "" ? null : s.trim());
const nextDay = (date: string) => {
  const d = new Date(`${date}T00:00:00Z`);
  d.setUTCDate(d.getUTCDate() + 1);
  return d.toISOString().slice(0, 10);
};

/** Form values → API body. Times are church-local; an end on the next day covers overnight events. */
export function toEventBody(v: EventValues) {
  return {
    eventTypeId: v.eventTypeId,
    groupId: v.groupId || null,
    title: blank(v.title),
    description: blank(v.description),
    startsAt: toInstant(v.date, v.start),
    endsAt: toInstant(v.endsNextDay ? nextDay(v.date) : v.date, v.end),
    location: blank(v.location),
    onlineLink: blank(v.onlineLink),
    repeatWeeks: v.repeatWeeks,
    audienceGroupIds: v.groupId ? [] : v.audienceGroupIds,
  };
}
