/**
 * מידות נפח — Volume units
 *
 * Base unit: כביצה (egg volume)
 *
 * The mnemonic כאסקלב יגודו (unverified — yeshiva tradition, not found in print sources)
 * may encode the order: כזית, אצבע(?), סאה, קב, לוג, ביצה, יין(?), גרוגרת, ועוד, דינר(?), ועוד
 * — could not be verified from any published source. Noted here for future research.
 *
 * Three regional standards [Talmud Eruvin 83a — Baraisa; confirmed by Zichru.com summary]:
 *   מדברית  (Sinai/Desert) — base standard: סאה = 144 ביצים
 *   ירושלמית (Jerusalem)   — 6/5 × מדברית:  סאה = 173 ביצים  (Baraisa: "יתירה שתות" = 1/6 more = ×6/5)
 *   ציפורית  (Tzipori)     — 6/5 × ירושלמית: סאה = 207 ביצים
 * Note: "שתות" in Talmudic usage here means 1/5 of the result (not 1/6 of the base),
 * i.e. ×6/5. Rashi on Eruvin 83a explains this explicitly.
 * All volume units in this file use the מדברית standard (the halachic default).
 *
 * Main chain [Wiki: Danby Mishnah Appendix II; TC; Rambam הקדמה למנחות]:
 *   לוג = 6 כביצות
 *   רביעית = 1/4 לוג = 1.5 כביצות [MB 271:סח; TC]
 *   קב = 4 לוגים = 24 כביצות
 *   סאה = 6 קבים = 144 כביצות
 *   איפה = 3 סאות = 432 כביצות [יחזקאל מה:יא]
 *   כור = 10 איפות = 4320 כביצות
 *   עומר = 1/10 איפה = 43.2 כביצות [שמות טז:לו]
 *   הין = 12 לוגים = 72 כביצות [במדבר טו:ד; TC]
 *   מקווה = 40 סאות = 5760 כביצות [תלמוד עירובין ד ב]
 *
 * Modern units stored in Naeh-space (anchor = ml / 54).
 * SI exact: 1 US fl oz = 29.5735295625 ml | 1 US cup = 236.5882365 ml
 *           1 US pint = 473.176473 ml | 1 US quart = 946.352946 ml | 1 US gallon = 3785.411784 ml
 *           1 tablespoon (US) = 14.78676478125 ml | 1 teaspoon (US) = 4.92892159375 ml
 */

import type { Unit } from './types'

export const VOLUME: Record<string, Unit> = {
  // ── Talmudic ───────────────────────────────────────────────────────────────
  קורטוב: { anchor: 6 / 64, system: 'volume', heDesc: '1/64 לוג' },
  משורה: { anchor: 6 / 32, system: 'volume', heDesc: '1/32 לוג' },
  גרוגרת: { anchor: 0.3, system: 'volume', heDesc: '3/10 כביצה (גודל תאנה יבשה)' },
  כזית: {
    anchor: 0.5,
    system: 'volume',
    heDesc: '1/2 כביצה',
    disputed: 'דעה אחרת: 1/3 כביצה [TC; פרנק, מילון תלמודי]',
  },
  כביצה: { anchor: 1, system: 'volume', heDesc: 'נפח ביצה' },
  עוכלא: { anchor: 1.2, system: 'volume', heDesc: '1/20 קב (= 1.2 כביצות) [TC]' },
  רביעית: { anchor: 1.5, system: 'volume', heDesc: '1/4 לוג' },
  תומן: { anchor: 3, system: 'volume', heDesc: '1/8 קב (= 2 רביעיות)' },
  פרס: {
    anchor: 4,
    system: 'volume',
    heDesc: 'רש"י: 4 כביצות',
    disputed: 'רמב"ם: 3 כביצות [SA OC 612:ד]',
  },
  לוג: { anchor: 6, system: 'volume', heDesc: '6 כביצות' },
  ליטרא: { anchor: 12, system: 'volume', heDesc: '2 לוגים' },
  עומר: { anchor: 43.2, system: 'volume', heDesc: '1/10 איפה (43.2 כביצות)' },
  קב: { anchor: 24, system: 'volume', heDesc: '4 לוגים' },
  הין: { anchor: 72, system: 'volume', heDesc: '12 לוגים' },
  סאה: { anchor: 144, system: 'volume', heDesc: '6 קבים (מדברית)' },
  'סאה ירושלמית': { anchor: 173, system: 'volume', heDesc: '6/5 × סאה מדברית [עירובין פג א]' },
  'סאה ציפורית': { anchor: 207, system: 'volume', heDesc: '6/5 × סאה ירושלמית [עירובין פג א]' },
  איפה: { anchor: 432, system: 'volume', heDesc: '3 סאות' },
  לתך: { anchor: 2160, system: 'volume', heDesc: '5 איפות' },
  כור: { anchor: 4320, system: 'volume', heDesc: '10 איפות' },
  מקווה: { anchor: 5760, system: 'volume', heDesc: '40 סאות' },

  // ── Modern metric [SI exact] ───────────────────────────────────────────────
  'מ"ל': { anchor: 1 / 54, system: 'volume', heDesc: 'מיליליטר' },
  "ל'": { anchor: 1000 / 54, system: 'volume', heDesc: 'ליטר' },

  // ── Imperial liquid [SI exact] ─────────────────────────────────────────────
  כפית: { anchor: 4.92892159375 / 54, system: 'volume', heDesc: 'teaspoon = 4.93 מ"ל' },
  כף: { anchor: 14.78676478125 / 54, system: 'volume', heDesc: 'tablespoon = 14.79 מ"ל' },
  'fl oz': { anchor: 29.5735295625 / 54, system: 'volume', heDesc: 'fluid ounce = 29.57 מ"ל' },
  כוס: { anchor: 236.5882365 / 54, system: 'volume', heDesc: 'cup = 236.6 מ"ל' },
  פינט: { anchor: 473.176473 / 54, system: 'volume', heDesc: 'pint = 473.2 מ"ל' },
  קווארט: { anchor: 946.352946 / 54, system: 'volume', heDesc: 'quart = 946.4 מ"ל' },
  גלון: { anchor: 3785.411784 / 54, system: 'volume', heDesc: 'gallon = 3.785 ל׳' },
}
