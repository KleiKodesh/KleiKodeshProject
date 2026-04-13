/**
 * מידות אורך — Length units
 *
 * Base unit: אצבע (finger-breadth)
 *
 * Talmudic chain [Wiki: תוספתא כלים מציעא ו:ד; TC; Rambam פירוש המשנה]:
 *   4 אצבעות = 1 טפח
 *   3 טפחים  = 1 זרת
 *   6 טפחים  = 1 אמה (= 2 זרתות)
 *   6 אמות   = 1 קנה  [TC; יחזקאל מ:ה]
 *   266⅔ אמות = 1 ריס (= 1600 טפחים) [Wiki; TC; רש"י בבא קמא עט ב; Rambam]
 *   2000 אמות = 1 מיל [Wiki: Rambam יומא ו:ד, רש"י; TC]
 *   4 מילים  = 1 פרסה [Wiki; TC]
 *   10 פרסאות = 1 דרך יום [TC; תלמוד פסחים צג ב]
 *
 * Modern units stored in Naeh-space (anchor = cm / 2.0).
 * SI exact definitions [SI]: 1 inch = 2.54 cm | 1 foot = 30.48 cm | 1 yard = 91.44 cm | 1 mile = 160934.4 cm
 */

import type { Unit } from './types'

export const LENGTH: Record<string, Unit> = {
  // ── Talmudic/Biblical ──────────────────────────────────────────────────────
  אצבע: { anchor: 1, system: 'length', heDesc: 'רוחב אצבע (אגודל)' },
  אגודל: { anchor: 1, system: 'length', heDesc: 'כאצבע' },
  טפח: { anchor: 4, system: 'length', heDesc: '4 אצבעות' },
  חסיט: { anchor: 8, system: 'length', heDesc: '2 טפחים' },
  זרת: { anchor: 12, system: 'length', heDesc: '3 טפחים' },
  אמה: { anchor: 24, system: 'length', heDesc: '6 טפחים' },
  'אמה בת חמשה': { anchor: 20, system: 'length', heDesc: '5 טפחים (אמה קצרה)' },
  גומד: { anchor: 20, system: 'length', heDesc: 'אמה קצרה (5 טפחים)' },
  קנה: { anchor: 144, system: 'length', heDesc: '6 אמות' },
  ריס: { anchor: 6400, system: 'length', heDesc: '266⅔ אמות' },
  מיל: { anchor: 48000, system: 'length', heDesc: '2000 אמות' },
  פרסה: { anchor: 192000, system: 'length', heDesc: '4 מילים' },
  'דרך יום': { anchor: 1920000, system: 'length', heDesc: '10 פרסאות' },

  // ── Modern metric [SI exact] ───────────────────────────────────────────────
  'מ"מ': { anchor: 0.1 / 2.0, system: 'length', heDesc: 'מילימטר' },
  'ס"מ': { anchor: 1 / 2.0, system: 'length', heDesc: 'סנטימטר' },
  "מ'": { anchor: 100 / 2.0, system: 'length', heDesc: 'מטר' },
  'ק"מ': { anchor: 100000 / 2.0, system: 'length', heDesc: 'קילומטר' },

  // ── Imperial [SI exact definitions] ───────────────────────────────────────
  "אינץ'": { anchor: 2.54 / 2.0, system: 'length', heDesc: 'inch = 2.54 ס"מ' },
  רגל: { anchor: 30.48 / 2.0, system: 'length', heDesc: 'foot = 30.48 ס"מ' },
  יארד: { anchor: 91.44 / 2.0, system: 'length', heDesc: 'yard = 91.44 ס"מ' },
  מייל: { anchor: 160934.4 / 2.0, system: 'length', heDesc: 'mile = 1.609 ק"מ' },
}
