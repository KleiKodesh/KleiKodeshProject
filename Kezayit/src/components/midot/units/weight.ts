import type { Unit, UnitSource } from './types'

const M: UnitSource = {
  label: 'המכלול',
  url: 'https://www.hamichlol.org.il/רשימת_מידות,_שיעורים_ומשקלות_בהלכה',
}
const B: UnitSource = { label: 'בניש, מידות ושיעורי תורה' }
const SI: UnitSource = { label: 'SI' }

export const WEIGHT: Record<string, Unit> = {
  גרה: {
    anchor: 1,
    system: 'weight',
    heDesc: '1/20 שקל',
    // המכלול: שקל = 9.6 גרם → גרה = 0.48 גרם
    // TorahCalc: גרה = 0.45 גרם — פער קטן
    source: M,
  },
  מעה: { anchor: 2, system: 'weight', heDesc: '2 גרות', source: M },
  דינר: { anchor: 10, system: 'weight', heDesc: '10 גרות (½ שקל)', source: M },
  בקע: { anchor: 10, system: 'weight', heDesc: '½ שקל', source: M },
  שקל: { anchor: 20, system: 'weight', heDesc: '20 גרות', source: M },
  סלע: { anchor: 40, system: 'weight', heDesc: '2 שקלים', source: M },
  אונקיא: { anchor: 80, system: 'weight', heDesc: '2 סלעים', source: M },
  תרטימר: {
    anchor: 500,
    system: 'weight',
    heDesc: '½ מנה (= 50 דינרים)',
    // המכלול טבלה: מנה = 480 גרם → ½ = 240 גרם
    // המכלול טקסט: "כ-180 גרם בשר" — הפרש בגלל שבשר קל ממים
    source: M,
  },
  מנה: { anchor: 1000, system: 'weight', heDesc: '100 דינרים', source: M },
  ליטרא: { anchor: 1000, system: 'weight', heDesc: 'מנה איטלקי (= מנה)', source: M },
  כיכר: { anchor: 60000, system: 'weight', heDesc: '60 מנה', source: M },
  גרם: { anchor: 1 / 0.48, system: 'weight', heDesc: 'גרם', source: SI },
  'ק"ג': { anchor: 1000 / 0.48, system: 'weight', heDesc: 'קילוגרם', source: SI },
  טון: { anchor: 1000000 / 0.48, system: 'weight', heDesc: 'טון מטרי', source: SI },
  אונס: { anchor: 28.349523125 / 0.48, system: 'weight', heDesc: 'ounce = 28.35 גרם', source: B },
  ליברה: { anchor: 453.59237 / 0.48, system: 'weight', heDesc: 'pound = 453.6 גרם', source: B },
  סטון: { anchor: 6350.29318 / 0.48, system: 'weight', heDesc: 'stone = 6.35 ק"ג', source: SI },
  'אונקיה טרוי': {
    anchor: 31.1034768 / 0.48,
    system: 'weight',
    heDesc: 'troy oz = 31.1 גרם',
    source: B,
  },
}
