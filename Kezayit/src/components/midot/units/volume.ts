/**
 * מידות נפח — Volume units
 * Base: כביצה
 *
 * המנמוניקה "כאסקלב יגודו" [המכלול]:
 *   כ=כור, א=איפה, ס=סאה, ק=קב, ל=לוג, ב=ביצה
 *   יגודו = י(10)×ג(3)×ו(6)×ד(4)×ו(6)
 *
 * שלוש מידות אזוריות [תלמוד עירובין פג א; המכלול]:
 *   מדברית: 144 | ירושלמית: ×6/5 = 173 | ציפורית: ×6/5 = 207
 *
 * עוכלא — מחלוקת:
 *   שיטה א (המכלול): 1/8 לוג = 0.75 ביצות
 *   שיטה ב (TorahCalc): 1/20 קב = 1.2 ביצות
 */

import type { Unit, UnitSource } from './types'

const M: UnitSource = {
  label: 'המכלול',
  url: 'https://www.hamichlol.org.il/רשימת_מידות,_שיעורים_ומשקלות_בהלכה',
}
const TC: UnitSource = { label: 'TorahCalc', url: 'https://www.torahcalc.com/info/biblical-units/' }
const H: UnitSource = { label: 'הידורי המידות' }
const B: UnitSource = { label: 'בניש, מידות ושיעורי תורה' }
const SI: UnitSource = { label: 'SI' }

export const VOLUME: Record<string, Unit> = {
  קורטוב: { anchor: 6 / 64, system: 'volume', heDesc: '1/64 לוג', source: M },
  משורה: { anchor: 6 / 32, system: 'volume', heDesc: '1/32 לוג', source: TC },
  גרוגרת: { anchor: 0.3, system: 'volume', heDesc: '3/10 כביצה', source: M },
  כזית: {
    anchor: 0.5,
    system: 'volume',
    heDesc: '½ כביצה (דאורייתא)',
    disputed: 'רמב"ם: פחות מ-1/3 כביצה (דרבנן)',
    source: M,
  },
  כביצה: {
    anchor: 1,
    system: 'volume',
    heDesc: 'נפח ביצה',
    source: H,
  },
  עוכלא: {
    anchor: 0.75,
    system: 'volume',
    heDesc: '1/8 לוג (= שמינית)',
    disputed: 'שיטה ב: 1/20 קב = 1.2 ביצות [TorahCalc]',
    source: M,
  },
  רביעית: {
    anchor: 1.5,
    system: 'volume',
    heDesc: '1/4 לוג',
    source: H,
  },
  תומן: { anchor: 3, system: 'volume', heDesc: '1/8 קב (= 2 רביעיות)', source: M },
  'פרס (רש"י)': {
    anchor: 4,
    system: 'volume',
    heDesc: '4 כביצות — שיטת רש"י',
    disputed: 'רמב"ם: 3 כביצות',
    source: M,
  },
  'פרס (רמב"ם)': {
    anchor: 3,
    system: 'volume',
    heDesc: '3 כביצות — שיטת רמב"ם',
    disputed: 'רש"י: 4 כביצות',
    source: M,
  },
  לוג: { anchor: 6, system: 'volume', heDesc: '6 כביצות', source: M },
  ליטרא: { anchor: 3, system: 'volume', heDesc: '2 רביעיות', source: M },
  עומר: { anchor: 43.2, system: 'volume', heDesc: '1/10 איפה', source: M },
  קב: { anchor: 24, system: 'volume', heDesc: '4 לוגים', source: M },
  הין: { anchor: 72, system: 'volume', heDesc: '12 לוגים', source: M },
  סאה: { anchor: 144, system: 'volume', heDesc: '6 קבים (מדברית)', source: M },
  'סאה ירושלמית': { anchor: 173, system: 'volume', heDesc: '6/5 × סאה מדברית', source: M },
  'סאה ציפורית': { anchor: 207, system: 'volume', heDesc: '6/5 × סאה ירושלמית', source: M },
  איפה: { anchor: 432, system: 'volume', heDesc: '3 סאות', source: M },
  לתך: { anchor: 2160, system: 'volume', heDesc: '5 איפות', source: M },
  כור: { anchor: 4320, system: 'volume', heDesc: '10 איפות', source: M },
  מקווה: { anchor: 5760, system: 'volume', heDesc: '40 סאות', source: M },
  'מ"ל': { anchor: 1 / 57.6, system: 'volume', heDesc: 'מיליליטר', source: SI },
  "ל'": { anchor: 1000 / 57.6, system: 'volume', heDesc: 'ליטר', source: SI },
  כפית: {
    anchor: 4.92892159375 / 57.6,
    system: 'volume',
    heDesc: 'teaspoon = 4.93 מ"ל',
    source: SI,
  },
  כף: {
    anchor: 14.78676478125 / 57.6,
    system: 'volume',
    heDesc: 'tablespoon = 14.79 מ"ל',
    source: SI,
  },
  'fl oz': {
    anchor: 29.5735295625 / 57.6,
    system: 'volume',
    heDesc: 'fluid ounce = 29.57 מ"ל',
    source: SI,
  },
  כוס: { anchor: 236.5882365 / 57.6, system: 'volume', heDesc: 'cup = 236.6 מ"ל', source: SI },
  פינט: { anchor: 473.176473 / 57.6, system: 'volume', heDesc: 'pint = 473.2 מ"ל', source: SI },
  קווארט: { anchor: 946.352946 / 57.6, system: 'volume', heDesc: 'quart = 946.4 מ"ל', source: SI },
  גלון: { anchor: 3785.411784 / 57.6, system: 'volume', heDesc: 'US gallon = 3.785 ל׳', source: B },
  'UK fl oz': {
    anchor: 28.4130625 / 57.6,
    system: 'volume',
    heDesc: 'UK fluid ounce = 28.41 מ"ל',
    source: B,
  },
  'UK פינט': {
    anchor: 568.26125 / 57.6,
    system: 'volume',
    heDesc: 'UK pint = 568.3 מ"ל',
    source: B,
  },
  'UK קווארט': {
    anchor: 1136.5225 / 57.6,
    system: 'volume',
    heDesc: 'UK quart = 1.137 ל׳',
    source: B,
  },
  'UK גאלון': {
    anchor: 4546.09 / 57.6,
    system: 'volume',
    heDesc: 'UK gallon = 4.546 ל׳',
    source: B,
  },
}
