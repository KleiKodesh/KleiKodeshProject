/**
 * מידות זמן — Time units
 *
 * Base unit: רגע (moment)
 *
 * Talmudic chain [TC; Wiki; תלמוד ברכות]:
 *   חלק = 76 רגעים  [TC; לוח העיבור]
 *   שעה = 1080 חלקים = 82080 רגעים  [TC; Wiki]
 *   יום = 24 שעות = 1,969,920 רגעים
 *
 * Metric: 1 חלק = 3⅓ שניות [TC] → 1 רגע = 10/228 שניות ≈ 0.04386 שניות
 *         → 1 שנייה = 22.8 רגעים [TC: "22.8 rega'im is 1 second"]
 *
 * Modern units stored in רגע-space (anchor = seconds × 22.8).
 */

import type { Unit } from './types'

const SEC = 22.8 // רגעים per second [TC]
const MIN = SEC * 60
const HOUR = MIN * 60
const DAY = HOUR * 24

export const TIME: Record<string, Unit> = {
  // ── Talmudic ───────────────────────────────────────────────────────────────
  רגע: { anchor: 1, system: 'time', heDesc: '1/76 חלק (≈0.044 שנ׳)' },
  חלק: { anchor: 76, system: 'time', heDesc: '76 רגעים (≈3.33 שנ׳)' },
  עת: { anchor: 142.5, system: 'time', heDesc: '1/24 עונה' },
  שעה: { anchor: 82080, system: 'time', heDesc: '1080 חלקים' },
  עונה: { anchor: 82080 * 12, system: 'time', heDesc: '12 שעות (חצי יום)' },
  יום: { anchor: DAY, system: 'time', heDesc: '24 שעות' },
  שבוע: { anchor: DAY * 7, system: 'time', heDesc: '7 ימים' },
  חודש: { anchor: DAY * 29.5, system: 'time', heDesc: '29–30 ימים' },
  תקופה: { anchor: DAY * 91.25, system: 'time', heDesc: 'רבע שנה (≈91 יום)' },
  שנה: { anchor: DAY * 354, system: 'time', heDesc: '354 ימים (שנה לבנה)' },
  שמיטה: { anchor: DAY * 354 * 7, system: 'time', heDesc: '7 שנים' },
  יובל: { anchor: DAY * 354 * 50, system: 'time', heDesc: '50 שנה' },

  // ── Modern [SI] ────────────────────────────────────────────────────────────
  שנייה: { anchor: SEC, system: 'time', heDesc: 'שנייה' },
  דקה: { anchor: MIN, system: 'time', heDesc: '60 שניות' },
  'שעה (מודרנית)': { anchor: HOUR, system: 'time', heDesc: '60 דקות' },
  'יום (מודרני)': { anchor: DAY, system: 'time', heDesc: '24 שעות' },
  שבוע_מ: { anchor: DAY * 7, system: 'time', heDesc: '7 ימים' },
}
