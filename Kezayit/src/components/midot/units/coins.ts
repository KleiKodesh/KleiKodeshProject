/**
 * מטבעות — Talmudic coin units
 * Base: פרוטה
 * מנמוניקה "סדמפאפ דובבח" [המכלול]: ד(4)×ו(6)×ב(2)×ב(2)×ח(8)
 * שרשרת מלאה: משנה שקלים א:ג
 */

import type { Unit, UnitSource } from './types'

const M: UnitSource = {
  label: 'המכלול',
  url: 'https://www.hamichlol.org.il/רשימת_מידות,_שיעורים_ומשקלות_בהלכה',
}
const MS: UnitSource = { label: 'משנה שקלים א:ג' }
const TC: UnitSource = { label: 'TorahCalc', url: 'https://www.torahcalc.com/info/biblical-units/' }

export const COINS: Record<string, Unit> = {
  פרוטה: { anchor: 1, system: 'coins', heDesc: '1/8 איסר', source: MS },
  קונטרנק: { anchor: 2, system: 'coins', heDesc: '2 פרוטות', source: M },
  מסימס: { anchor: 4, system: 'coins', heDesc: '4 פרוטות', source: M },
  איסר: { anchor: 8, system: 'coins', heDesc: '8 פרוטות', source: MS },
  פונדיון: { anchor: 16, system: 'coins', heDesc: '2 איסרים', source: MS },
  מעה: { anchor: 32, system: 'coins', heDesc: '2 פונדיון', source: MS },
  גרה: { anchor: 32, system: 'coins', heDesc: 'כמעה (שם בתורה)', source: M },
  איסתרא: { anchor: 96, system: 'coins', heDesc: '3 מעות', source: M },
  טרפעיק: { anchor: 96, system: 'coins', heDesc: 'כאיסתרא', source: M },
  דינר: { anchor: 192, system: 'coins', heDesc: '6 מעות', source: MS },
  זוז: { anchor: 192, system: 'coins', heDesc: 'כדינר', source: M },
  שקל: { anchor: 384, system: 'coins', heDesc: '2 דינרים', source: MS },
  סלע: { anchor: 768, system: 'coins', heDesc: '4 דינרים', source: MS },
  דרכון: { anchor: 1536, system: 'coins', heDesc: '2 סלעים', source: TC },
  'דינר זהב': { anchor: 4800, system: 'coins', heDesc: '6¼ סלעים', source: M },
  מנה: { anchor: 19200, system: 'coins', heDesc: '100 דינרים', source: M },
  'מנה של קודש': { anchor: 38400, system: 'coins', heDesc: '2 מנה', source: M },
  כיכר: { anchor: 1152000, system: 'coins', heDesc: '60 מנה', source: M },
  'כיכר של קודש': { anchor: 2304000, system: 'coins', heDesc: '2 כיכר', source: M },
}
