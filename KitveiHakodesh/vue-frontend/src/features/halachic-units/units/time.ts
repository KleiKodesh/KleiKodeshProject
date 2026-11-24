import type { Unit, UnitSource } from './conversionUnitTypes'

const M: UnitSource = {
  label: 'המכלול',
  url: 'https://www.hamichlol.org.il/רשימת_מידות,_שיעורים_ומשקלות_בהלכה',
}
const TC: UnitSource = { label: 'TorahCalc', url: 'https://www.torahcalc.com/info/biblical-units/' }
const SI: UnitSource = { label: 'SI' }
const MIDOT_MEDUYAKOT: UnitSource = {
  label: 'מידות התורה המדויקות — שלמה ב.ד הכהן',
  url: 'https://tora-forum.co.il/attachments/מידות-התורה-המדויקות-pdf.101762/',
}

const LUACH: UnitSource = { label: 'לוח העיבור העברי' }

const SEC = 22.8
const MIN = SEC * 60
const HOUR = MIN * 60
const DAY = HOUR * 24

export const TIME: Record<string, Unit> = {
  רגע: { anchor: 1, system: 'time', heDesc: '1/76 חלק', talmudSource: LUACH, refSource: M },
  חלק: { anchor: 76, system: 'time', heDesc: '76 רגעים', talmudSource: LUACH, refSource: M },
  עת: { anchor: 142.5, system: 'time', heDesc: '1⅞ חלק', refSource: TC },
  שעה: { anchor: 82080, system: 'time', heDesc: '1080 חלקים', talmudSource: LUACH, refSource: M },
  עונה: { anchor: 82080 * 12, system: 'time', heDesc: '12 שעות', refSource: M },
  יום: { anchor: DAY, system: 'time', heDesc: '24 שעות', refSource: M },
  שבוע: { anchor: DAY * 7, system: 'time', heDesc: '7 ימים', refSource: M },
  חודש: { anchor: DAY * 29.5, system: 'time', heDesc: '29–30 ימים', refSource: M },
  תקופה: { anchor: DAY * 91.25, system: 'time', heDesc: 'רבע שנה', refSource: M },
  שנה: { anchor: DAY * 354, system: 'time', heDesc: '354 ימים', refSource: M },
  שמיטה: {
    anchor: DAY * 354 * 7,
    system: 'time',
    heDesc: '7 שנים',
    talmudSource: { label: 'ויקרא כה:ד' },
    refSource: M,
  },
  יובל: {
    anchor: DAY * 354 * 50,
    system: 'time',
    heDesc: '50 שנה',
    talmudSource: { label: 'ויקרא כה:י' },
    refSource: M,
  },
  שנייה: { anchor: SEC, system: 'time', heDesc: 'שנייה', refSource: SI },
  דקה: { anchor: MIN, system: 'time', heDesc: '60 שניות', refSource: SI },
  'שעה (מודרנית)': { anchor: HOUR, system: 'time', heDesc: '60 דקות', refSource: SI },
  'יום (מודרני)': { anchor: DAY, system: 'time', heDesc: '24 שעות', refSource: SI },
  שבוע_מ: { anchor: DAY * 7, system: 'time', heDesc: '7 ימים', refSource: SI },
  // ── שיעורי זמן מעשיים (מידות התורה המדויקות) ─────────────────────────────
  'כדי אכילת כזית': {
    anchor: SEC * 20,
    system: 'time',
    heDesc: '~20 שניות',
    talmudSource: { label: 'תלמוד בבלי' },
    refSource: MIDOT_MEDUYAKOT,
  },
  'כדי אכילת פרס': {
    anchor: MIN * 12,
    system: 'time',
    heDesc: '~12 דקות (פת בלפתן)',
    talmudSource: { label: 'משנה נגעים יג:ט; תלמוד בבלי עירובין ד א' },
    refSource: MIDOT_MEDUYAKOT,
  },
  'כדי הילוך מיל': {
    anchor: MIN * 22.5,
    system: 'time',
    heDesc: '~22.5 דקות (הליכה בינונית)',
    talmudSource: { label: 'רמב"ם, פירוש המשנה פסחים פ"ג; פ"ט' },
    refSource: MIDOT_MEDUYAKOT,
  },
}
