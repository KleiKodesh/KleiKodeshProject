import type { Unit, UnitSource } from './conversionUnitTypes'

// Reference sources
const M: UnitSource = {
  label: 'המכלול',
  url: 'https://www.hamichlol.org.il/רשימת_מידות,_שיעורים_ומשקלות_בהלכה',
}
const TC: UnitSource = { label: 'TorahCalc', url: 'https://www.torahcalc.com/info/biblical-units/' }
const SI: UnitSource = { label: 'SI — הסכם בינלאומי 1959' }

// Primary Talmudic sources
const TOSEFTA: UnitSource = { label: 'תוספתא כלים מציעא ו:ד' }
const RAMBAM_M: UnitSource = { label: 'רמב"ם, פירוש המשנה, כלאים ו:ו' }
const YECHEZKEL: UnitSource = { label: 'יחזקאל מ:ה' }

export const LENGTH: Record<string, Unit> = {
  אצבע: { anchor: 1, system: 'length', heDesc: 'רוחב אצבע', talmudSource: TOSEFTA, refSource: M },
  אגודל: { anchor: 1, system: 'length', heDesc: 'כאצבע', talmudSource: TOSEFTA, refSource: M },
  טפח: { anchor: 4, system: 'length', heDesc: '4 אצבעות', talmudSource: TOSEFTA, refSource: M },
  סיט: { anchor: 8, system: 'length', heDesc: '2 טפחים', talmudSource: TOSEFTA, refSource: M },
  זרת: {
    anchor: 12,
    system: 'length',
    heDesc: '3 טפחים',
    talmudSource: { label: 'שמות כח:טז; תוספתא כלים מציעא ו:ד' },
    refSource: M,
  },
  אמה: { anchor: 24, system: 'length', heDesc: '6 טפחים', talmudSource: TOSEFTA, refSource: M },
  'אמה בת חמשה': {
    anchor: 20,
    system: 'length',
    heDesc: '5 טפחים',
    talmudSource: { label: 'יחזקאל מג:יג' },
    refSource: TC,
  },
  גומד: {
    anchor: 20,
    system: 'length',
    heDesc: 'אמה קצרה',
    talmudSource: { label: 'שופטים ג:טז' },
    refSource: M,
  },
  קנה: { anchor: 144, system: 'length', heDesc: '6 אמות', talmudSource: YECHEZKEL, refSource: M },
  ריס: {
    anchor: 6400,
    system: 'length',
    heDesc: '266⅔ אמות',
    talmudSource: { label: 'רש"י בבא קמא עט ב; רמב"ם יומא ו:ד' },
    refSource: M,
  },
  מיל: {
    anchor: 48000,
    system: 'length',
    heDesc: '2000 אמות',
    talmudSource: RAMBAM_M,
    refSource: M,
  },
  פרסה: {
    anchor: 192000,
    system: 'length',
    heDesc: '4 מילים',
    talmudSource: { label: 'תלמוד בבלי פסחים צג ב' },
    refSource: M,
  },
  'דרך יום': {
    anchor: 1920000,
    system: 'length',
    heDesc: '10 פרסאות',
    talmudSource: { label: 'תלמוד בבלי פסחים צג ב' },
    refSource: TC,
  },
  'מ"מ': { anchor: 0.1 / 2.0, system: 'length', heDesc: 'מילימטר', refSource: SI },
  'ס"מ': { anchor: 1 / 2.0, system: 'length', heDesc: 'סנטימטר', refSource: SI },
  "מ'": { anchor: 100 / 2.0, system: 'length', heDesc: 'מטר', refSource: SI },
  'ק"מ': { anchor: 100000 / 2.0, system: 'length', heDesc: 'קילומטר', refSource: SI },
  "אינץ'": { anchor: 2.54 / 2.0, system: 'length', heDesc: 'inch = 2.54 ס"מ', refSource: SI },
  רגל: { anchor: 30.48 / 2.0, system: 'length', heDesc: 'foot = 30.48 ס"מ', refSource: SI },
  יארד: { anchor: 91.44 / 2.0, system: 'length', heDesc: 'yard = 91.44 ס"מ', refSource: SI },
  מייל: { anchor: 160934.4 / 2.0, system: 'length', heDesc: 'mile = 1.609 ק"מ', refSource: SI },
}
