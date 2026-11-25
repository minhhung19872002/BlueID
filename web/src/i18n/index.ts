import i18n from "i18next";
import { initReactI18next } from "react-i18next";
import en from "./locales/en.json";
import vi from "./locales/vi.json";

export const languages = ["en", "vi"] as const;
export type Language = (typeof languages)[number];

/** Right-to-left scripts; the layout uses logical properties so adding such a language needs only its file. */
const rtl = new Set(["ar", "he", "fa", "ur"]);

export function isRtl(lang: string): boolean {
  return rtl.has(lang.split("-")[0] ?? lang);
}

/** The user's setting, then the tenant default, then the browser (PROMPT 0.2). */
export function pickLanguage(userLocale: string | null, tenantDefault: string | null): Language {
  for (const candidate of [userLocale, tenantDefault, ...navigator.languages]) {
    const base = candidate?.split("-")[0];
    const match = languages.find((l) => l === base);
    if (match) {
      return match;
    }
  }
  return "en";
}

void i18n.use(initReactI18next).init({
  resources: { en: { translation: en }, vi: { translation: vi } },
  lng: pickLanguage(null, null),
  fallbackLng: "en",
  interpolation: { escapeValue: false },
});

export default i18n;
