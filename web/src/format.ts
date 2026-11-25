// Dates and times shown to a person are in their own time zone and hour cycle (lesson 11).

export interface DisplayPrefs {
  locale: string;
  timeZone: string;
  hourCycle: "h12" | "h23" | null;
}

export function formatDateTime(iso: string | null, p: DisplayPrefs): string {
  if (!iso) {
    return "—";
  }
  return new Intl.DateTimeFormat(p.locale, {
    dateStyle: "medium",
    timeStyle: "short",
    timeZone: p.timeZone,
    ...(p.hourCycle ? { hourCycle: p.hourCycle } : {}),
  }).format(new Date(iso));
}

/** IANA zones the browser knows (Intl.supportedValuesOf), with UTC first. */
export function timeZones(): string[] {
  const all = (Intl as unknown as { supportedValuesOf?: (k: string) => string[] }).supportedValuesOf?.("timeZone") ?? [];
  return ["UTC", ...all.filter((z) => z !== "UTC")];
}
