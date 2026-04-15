import type { Unit, UnitSource } from './types'

const M: UnitSource = {
  label: 'המכלול',
  url: 'https://www.hamichlol.org.il/רשימת_מידות,_שיעורים_ומשקלות_בהלכה',
}
const B: UnitSource = { label: 'בניש, מידות ושיעורי תורה' }
const SI: UnitSource = { label: 'SI' }

const SHEMOT_30: UnitSource = { label: 'שמות ל:יג' }
const SHEMOT_38: UnitSource = { label: 'שמות לח:כה-כו' }
const YECHEZKEL: UnitSource = { label: 'יחזקאל מה:יב' }

export const WEIGHT: Record<string, Unit> = {
  גרה: { anchor: 1, system: 'weight', heDesc: '1/20 שקל', talmudSource: SHEMOT_30, refSource: M },
  מעה: {
    anchor: 2,
    system: 'weight',
    heDesc: '2 גרות',
    talmudSource: { label: 'משנה שקלים א:ג' },
    refSource: M,
  },
  דינר: { anchor: 10, system: 'weight', heDesc: '10 גרות (½ שקל)', refSource: M },
  בקע: {
    anchor: 10,
    system: 'weight',
    heDesc: '½ שקל',
    talmudSource: { label: 'שמות לח:כו' },
    refSource: M,
  },
  שקל: { anchor: 20, system: 'weight', heDesc: '20 גרות', talmudSource: SHEMOT_30, refSource: M },
  סלע: {
    anchor: 40,
    system: 'weight',
    heDesc: '2 שקלים',
    talmudSource: { label: 'משנה שקלים א:ג' },
    refSource: M,
  },
  אונקיא: { anchor: 80, system: 'weight', heDesc: '2 סלעים', refSource: M },
  תרטימר: {
    anchor: 500,
    system: 'weight',
    heDesc: '½ מנה',
    talmudSource: { label: 'משנה סנהדרין ח:ב' },
    refSource: M,
  },
  מנה: {
    anchor: 1000,
    system: 'weight',
    heDesc: '100 דינרים',
    talmudSource: YECHEZKEL,
    refSource: M,
  },
  ליטרא: { anchor: 1000, system: 'weight', heDesc: 'מנה איטלקי', refSource: M },
  כיכר: {
    anchor: 60000,
    system: 'weight',
    heDesc: '60 מנה',
    talmudSource: SHEMOT_38,
    refSource: M,
  },
  גרם: { anchor: 1 / 0.48, system: 'weight', heDesc: 'גרם', refSource: SI },
  'ק"ג': { anchor: 1000 / 0.48, system: 'weight', heDesc: 'קילוגרם', refSource: SI },
  טון: { anchor: 1000000 / 0.48, system: 'weight', heDesc: 'טון מטרי', refSource: SI },
  אונס: {
    anchor: 28.349523125 / 0.48,
    system: 'weight',
    heDesc: 'ounce = 28.35 גרם',
    refSource: B,
  },
  ליברה: { anchor: 453.59237 / 0.48, system: 'weight', heDesc: 'pound = 453.6 גרם', refSource: B },
  סטון: { anchor: 6350.29318 / 0.48, system: 'weight', heDesc: 'stone = 6.35 ק"ג', refSource: SI },
  'אונקיה טרוי': {
    anchor: 31.1034768 / 0.48,
    system: 'weight',
    heDesc: 'troy oz = 31.1 גרם',
    refSource: B,
  },
}
