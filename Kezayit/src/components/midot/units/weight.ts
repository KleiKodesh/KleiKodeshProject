/**
 * מידות משקל — Weight units
 *
 * Base unit: גרה
 *
 * Biblical chain [שמות ל:יג; ויקרא כז:כה; במדבר ג:מז — cited in TC, Wiki, SA]:
 *   שקל = 20 גרות
 *   סלע = 2 שקלים = 40 גרות
 *   אונקיא = 2 סלעים = 80 גרות
 *   תרטימר = 1/2 מנה = 500 גרות
 *   מנה/ליטרא = 60 שקלים = 1200 גרות [TC; Rambam; Wiki]
 *   כיכר = 60 מנה = 72000 גרות [Wiki; TC; שמות לח:כה-כו]
 *
 * Metric per opinion [TC]:
 *   Naeh:        גרה = 0.7 גרם  (שקל ≈ 14 גרם)
 *   Chazon Ish:  גרה = 0.85 גרם (שקל ≈ 17 גרם)
 *
 * Modern units stored in Naeh-space (anchor = grams / 0.7).
 * SI exact: 1 oz (avoirdupois) = 28.349523125 g | 1 lb = 453.59237 g | 1 kg = 1000 g
 */

import type { Unit } from './types'

export const WEIGHT: Record<string, Unit> = {
  // ── Talmudic/Biblical ──────────────────────────────────────────────────────
  גרה: { anchor: 1, system: 'weight', heDesc: '1/20 שקל [שמות ל:יג]' },
  מעה: { anchor: 2, system: 'weight', heDesc: '2 גרות' },
  דינר: { anchor: 10, system: 'weight', heDesc: '10 גרות (1/2 שקל)' },
  שקל: { anchor: 20, system: 'weight', heDesc: '20 גרות [שמות ל:יג]' },
  בקע: { anchor: 10, system: 'weight', heDesc: '1/2 שקל [שמות לח:כו]' },
  סלע: { anchor: 40, system: 'weight', heDesc: '2 שקלים' },
  אונקיא: { anchor: 80, system: 'weight', heDesc: '2 סלעים' },
  תרטימר: { anchor: 500, system: 'weight', heDesc: '1/2 מנה' },
  מנה: { anchor: 1200, system: 'weight', heDesc: '60 שקלים' },
  ליטרא: { anchor: 1200, system: 'weight', heDesc: 'מנה איטלקי (= מנה)' },
  כיכר: { anchor: 72000, system: 'weight', heDesc: '60 מנה [שמות לח:כה]' },

  // ── Modern metric [SI exact] ───────────────────────────────────────────────
  גרם: { anchor: 1 / 0.7, system: 'weight', heDesc: 'גרם' },
  'ק"ג': { anchor: 1000 / 0.7, system: 'weight', heDesc: 'קילוגרם' },
  טון: { anchor: 1000000 / 0.7, system: 'weight', heDesc: 'טון מטרי' },

  // ── Imperial [SI exact] ────────────────────────────────────────────────────
  אונס: { anchor: 28.349523125 / 0.7, system: 'weight', heDesc: 'ounce = 28.35 גרם' },
  ליברה: { anchor: 453.59237 / 0.7, system: 'weight', heDesc: 'pound = 453.6 גרם' },
  סטון: { anchor: 6350.29318 / 0.7, system: 'weight', heDesc: 'stone = 6.35 ק"ג' },
}
