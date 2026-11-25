import { describe, expect, it } from "vitest";
import { formatDateTime } from "./format";
import en from "./i18n/locales/en.json";
import vi from "./i18n/locales/vi.json";

describe("formatDateTime", () => {
  it("uses the user's time zone and hour cycle", () => {
    const iso = "2026-11-25T01:30:00Z";
    expect(formatDateTime(iso, { locale: "en-GB", timeZone: "Asia/Ho_Chi_Minh", hourCycle: "h23" })).toContain("08:30");
    expect(formatDateTime(iso, { locale: "en-US", timeZone: "America/New_York", hourCycle: "h12" })).toMatch(/8:30\s?PM/);
  });

  it("shows a dash for missing values", () => {
    expect(formatDateTime(null, { locale: "en", timeZone: "UTC", hourCycle: null })).toBe("—");
  });
});

function keys(o: object, prefix = ""): string[] {
  return Object.entries(o).flatMap(([k, v]) => (typeof v === "object" && v !== null ? keys(v as object, `${prefix}${k}.`) : [`${prefix}${k}`]));
}

describe("translations", () => {
  it("vi has exactly the en keys, at every level", () => {
    expect(keys(vi).sort()).toEqual(keys(en).sort());
  });
});
