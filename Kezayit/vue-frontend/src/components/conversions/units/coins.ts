import type { Unit, UnitSource } from './types'

const M: UnitSource = {
  label: 'המכלול',
  url: 'https://www.hamichlol.org.il/רשימת_מידות,_שיעורים_ומשקלות_בהלכה',
}
const TC: UnitSource = { label: 'TorahCalc', url: 'https://www.torahcalc.com/info/biblical-units/' }

const SHEKALIM: UnitSource = { label: 'משנה שקלים א:ג' }
const BECHOROT: UnitSource = { label: 'תלמוד בבלי בכורות מט ב' }

export const COINS: Record<string, Unit> = {
  פרוטה: { anchor: 1, system: 'coins', heDesc: '1/8 איסר', talmudSource: SHEKALIM, refSource: M },
  קונטרנק: { anchor: 2, system: 'coins', heDesc: '2 פרוטות', refSource: M },
  מסימס: { anchor: 4, system: 'coins', heDesc: '4 פרוטות', refSource: M },
  איסר: { anchor: 8, system: 'coins', heDesc: '8 פרוטות', talmudSource: SHEKALIM, refSource: M },
  פונדיון: {
    anchor: 16,
    system: 'coins',
    heDesc: '2 איסרים',
    talmudSource: SHEKALIM,
    refSource: M,
  },
  מעה: { anchor: 32, system: 'coins', heDesc: '2 פונדיון', talmudSource: SHEKALIM, refSource: M },
  גרה: {
    anchor: 32,
    system: 'coins',
    heDesc: 'כמעה (שם בתורה)',
    talmudSource: { label: 'שמות ל:יג' },
    refSource: M,
  },
  איסתרא: { anchor: 96, system: 'coins', heDesc: '3 מעות', refSource: M },
  טרפעיק: { anchor: 96, system: 'coins', heDesc: 'כאיסתרא', refSource: M },
  דינר: { anchor: 192, system: 'coins', heDesc: '6 מעות', talmudSource: SHEKALIM, refSource: M },
  זוז: { anchor: 192, system: 'coins', heDesc: 'כדינר', refSource: M },
  שקל: {
    anchor: 384,
    system: 'coins',
    heDesc: '2 דינרים',
    talmudSource: { label: 'משנה שקלים א:ב' },
    refSource: M,
  },
  סלע: { anchor: 768, system: 'coins', heDesc: '4 דינרים', talmudSource: SHEKALIM, refSource: M },
  דרכון: { anchor: 1536, system: 'coins', heDesc: '2 סלעים', refSource: TC },
  'דינר זהב': {
    anchor: 4800,
    system: 'coins',
    heDesc: '6¼ סלעים',
    talmudSource: { label: 'תלמוד בבלי בכורות נ א' },
    refSource: M,
  },
  מנה: {
    anchor: 19200,
    system: 'coins',
    heDesc: '100 דינרים',
    talmudSource: BECHOROT,
    refSource: M,
  },
  'מנה של קודש': { anchor: 38400, system: 'coins', heDesc: '2 מנה', refSource: M },
  כיכר: { anchor: 1152000, system: 'coins', heDesc: '60 מנה', refSource: M },
  'כיכר של קודש': { anchor: 2304000, system: 'coins', heDesc: '2 כיכר', refSource: M },
}
