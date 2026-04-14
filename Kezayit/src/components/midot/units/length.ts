import type { Unit, UnitSource } from './types'

const M: UnitSource = {
  label: 'המכלול',
  url: 'https://www.hamichlol.org.il/רשימת_מידות,_שיעורים_ומשקלות_בהלכה',
}
const TC: UnitSource = { label: 'TorahCalc', url: 'https://www.torahcalc.com/info/biblical-units/' }
const H: UnitSource = { label: 'הידורי המידות' }
const SI: UnitSource = { label: 'SI' }

export const LENGTH: Record<string, Unit> = {
  אצבע: { anchor: 1, system: 'length', heDesc: 'רוחב אצבע', source: M },
  אגודל: { anchor: 1, system: 'length', heDesc: 'כאצבע', source: M },
  טפח: { anchor: 4, system: 'length', heDesc: '4 אצבעות', source: M },
  סיט: { anchor: 8, system: 'length', heDesc: '2 טפחים', source: M },
  זרת: { anchor: 12, system: 'length', heDesc: '3 טפחים', source: M },
  אמה: { anchor: 24, system: 'length', heDesc: '6 טפחים', source: M },
  'אמה בת חמשה': { anchor: 20, system: 'length', heDesc: '5 טפחים', source: TC },
  גומד: { anchor: 20, system: 'length', heDesc: 'אמה קצרה', source: M },
  קנה: { anchor: 144, system: 'length', heDesc: '6 אמות', source: M },
  ריס: { anchor: 6400, system: 'length', heDesc: '266⅔ אמות', source: M },
  מיל: { anchor: 48000, system: 'length', heDesc: '2000 אמות', source: M },
  פרסה: { anchor: 192000, system: 'length', heDesc: '4 מילים', source: M },
  'דרך יום': { anchor: 1920000, system: 'length', heDesc: '10 פרסאות', source: TC },
  'מ"מ': { anchor: 0.1 / 2.0, system: 'length', heDesc: 'מילימטר', source: SI },
  'ס"מ': { anchor: 1 / 2.0, system: 'length', heDesc: 'סנטימטר', source: SI },
  "מ'": { anchor: 100 / 2.0, system: 'length', heDesc: 'מטר', source: SI },
  'ק"מ': { anchor: 100000 / 2.0, system: 'length', heDesc: 'קילומטר', source: SI },
  "אינץ'": { anchor: 2.54 / 2.0, system: 'length', heDesc: 'inch = 2.54 ס"מ', source: SI },
  רגל: { anchor: 30.48 / 2.0, system: 'length', heDesc: 'foot = 30.48 ס"מ', source: SI },
  יארד: { anchor: 91.44 / 2.0, system: 'length', heDesc: 'yard = 91.44 ס"מ', source: SI },
  מייל: { anchor: 160934.4 / 2.0, system: 'length', heDesc: 'mile = 1.609 ק"מ', source: SI },
}
