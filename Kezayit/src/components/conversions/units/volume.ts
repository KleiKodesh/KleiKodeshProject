import type { Unit, UnitSource } from './types'

const M: UnitSource = {
  label: 'המכלול',
  url: 'https://www.hamichlol.org.il/רשימת_מידות,_שיעורים_ומשקלות_בהלכה',
}
const TC: UnitSource = { label: 'TorahCalc', url: 'https://www.torahcalc.com/info/biblical-units/' }
const H: UnitSource = { label: 'הידורי המידות' }
const B: UnitSource = { label: 'בניש, מידות ושיעורי תורה' }
const SI: UnitSource = { label: 'SI' }

const MIDOT_MEDUYAKOT: UnitSource = {
  label: 'מידות התורה המדויקות — שלמה ב.ד הכהן',
  url: 'https://tora-forum.co.il/attachments/מידות-התורה-המדויקות-pdf.101762/',
}

const PESACHIM: UnitSource = { label: 'תלמוד בבלי פסחים קט א' }
const ERUVIN83: UnitSource = { label: 'תלמוד בבלי עירובין פג א' }
const RAMBAM_M: UnitSource = { label: 'רמב"ם, הקדמה למנחות' }
const SA_486: UnitSource = { label: 'שולחן ערוך OC תפו:א' }
const SA_612: UnitSource = { label: 'שולחן ערוך OC תריב:ד' }

export const VOLUME: Record<string, Unit> = {
  קורטוב: {
    anchor: 6 / 64,
    system: 'volume',
    heDesc: '1/64 לוג',
    talmudSource: { label: 'תלמוד בבלי' },
    refSource: M,
  },
  משורה: { anchor: 6 / 32, system: 'volume', heDesc: '1/32 לוג', refSource: TC },
  גרוגרת: {
    anchor: 0.3,
    system: 'volume',
    heDesc: '3/10 כביצה',
    talmudSource: { label: 'שולחן ערוך OC שסח' },
    refSource: M,
  },
  כזית: {
    anchor: 0.5,
    system: 'volume',
    heDesc: '½ כביצה (דאורייתא)',
    disputed: 'רמב"ם: פחות מ-1/3 כביצה (דרבנן)',
    talmudSource: { label: 'תוספות יומא פ א; שולחן ערוך OC תפו:א' },
    refSource: M,
  },
  כביצה: {
    anchor: 1,
    system: 'volume',
    heDesc: 'נפח ביצה',
    talmudSource: ERUVIN83,
    refSource: H,
  },
  עוכלא: {
    anchor: 0.75,
    system: 'volume',
    heDesc: '1/8 לוג (= שמינית)',
    disputed: 'שיטה ב: 1/20 קב = 1.2 ביצות [TorahCalc]',
    talmudSource: { label: 'תלמוד בבלי' },
    refSource: M,
  },
  רביעית: {
    anchor: 1.5,
    system: 'volume',
    heDesc: '1/4 לוג',
    talmudSource: PESACHIM,
    refSource: H,
  },
  תומן: {
    anchor: 3,
    system: 'volume',
    heDesc: '1/8 קב',
    talmudSource: { label: 'תלמוד בבלי' },
    refSource: M,
  },
  'פרס (רש"י)': {
    anchor: 4,
    system: 'volume',
    heDesc: '4 כביצות — שיטת רש"י',
    disputed: 'רמב"ם: 3 כביצות',
    talmudSource: SA_612,
    refSource: M,
  },
  'פרס (רמב"ם)': {
    anchor: 3,
    system: 'volume',
    heDesc: '3 כביצות — שיטת רמב"ם',
    disputed: 'רש"י: 4 כביצות',
    talmudSource: SA_612,
    refSource: M,
  },
  לוג: { anchor: 6, system: 'volume', heDesc: '6 כביצות', talmudSource: RAMBAM_M, refSource: M },
  ליטרא: { anchor: 3, system: 'volume', heDesc: '2 רביעיות', refSource: M },
  עומר: {
    anchor: 43.2,
    system: 'volume',
    heDesc: '1/10 איפה',
    talmudSource: { label: 'שמות טז:לו' },
    refSource: M,
  },
  קב: {
    anchor: 24,
    system: 'volume',
    heDesc: '4 לוגים',
    talmudSource: { label: 'משנה' },
    refSource: M,
  },
  הין: {
    anchor: 72,
    system: 'volume',
    heDesc: '12 לוגים',
    talmudSource: { label: 'במדבר טו:ד' },
    refSource: M,
  },
  סאה: {
    anchor: 144,
    system: 'volume',
    heDesc: '6 קבים (מדברית)',
    talmudSource: ERUVIN83,
    refSource: M,
  },
  'סאה ירושלמית': {
    anchor: 173,
    system: 'volume',
    heDesc: '6/5 × סאה מדברית',
    talmudSource: ERUVIN83,
    refSource: M,
  },
  'סאה ציפורית': {
    anchor: 207,
    system: 'volume',
    heDesc: '6/5 × סאה ירושלמית',
    talmudSource: ERUVIN83,
    refSource: M,
  },
  איפה: {
    anchor: 432,
    system: 'volume',
    heDesc: '3 סאות',
    talmudSource: { label: 'יחזקאל מה:יא' },
    refSource: M,
  },
  לתך: {
    anchor: 2160,
    system: 'volume',
    heDesc: '5 איפות',
    talmudSource: { label: 'הושע ג:ב' },
    refSource: M,
  },
  כור: {
    anchor: 4320,
    system: 'volume',
    heDesc: '10 איפות',
    talmudSource: { label: 'יחזקאל מה:יד' },
    refSource: M,
  },
  מקווה: {
    anchor: 5760,
    system: 'volume',
    heDesc: '40 סאות',
    talmudSource: { label: 'תלמוד בבלי עירובין ד ב' },
    refSource: M,
  },
  // ── שיעורי כתמים ונגעים (מידות התורה המדויקות) ───────────────────────────
  // אלו שיעורי שטח המבוטאים כקוטר עיגול; anchor = נפח ביחס לכביצה (נאה-space)
  // גריס הקלקי: קוטר 13.537 מ"מ → שטח 143.9 מ"מ² → נפח ≈ 0.25 כביצה (לצורך המרה)
  'גריס הקלקי': {
    anchor: 0.25,
    system: 'volume',
    heDesc: 'קוטר 13.537 מ"מ — שיעור כתמים ונגעים',
    talmudSource: { label: 'רמב"ם, הלכות אבות הטומאות ב:יז; נגעים א:ז' },
    refSource: MIDOT_MEDUYAKOT,
  },
  'גריס סתם': {
    anchor: 0.114,
    system: 'volume',
    heDesc: 'קוטר 6.52 מ"מ — גריס רגיל (לתליה במאכולת)',
    talmudSource: { label: 'תלמוד בבלי נדה נח ב' },
    refSource: MIDOT_MEDUYAKOT,
  },
  תורמוס: {
    anchor: 0.153,
    system: 'volume',
    heDesc: 'קוטר 9.2 מ"מ',
    talmudSource: { label: 'תלמוד בבלי' },
    refSource: MIDOT_MEDUYAKOT,
  },
  'מ"ל': { anchor: 1 / 57.6, system: 'volume', heDesc: 'מיליליטר', refSource: SI },
  "ל'": { anchor: 1000 / 57.6, system: 'volume', heDesc: 'ליטר', refSource: SI },
  כפית: {
    anchor: 4.92892159375 / 57.6,
    system: 'volume',
    heDesc: 'teaspoon = 4.93 מ"ל',
    refSource: SI,
  },
  כף: {
    anchor: 14.78676478125 / 57.6,
    system: 'volume',
    heDesc: 'tablespoon = 14.79 מ"ל',
    refSource: SI,
  },
  'fl oz': {
    anchor: 29.5735295625 / 57.6,
    system: 'volume',
    heDesc: 'fluid ounce = 29.57 מ"ל',
    refSource: SI,
  },
  כוס: { anchor: 236.5882365 / 57.6, system: 'volume', heDesc: 'cup = 236.6 מ"ל', refSource: SI },
  פינט: { anchor: 473.176473 / 57.6, system: 'volume', heDesc: 'pint = 473.2 מ"ל', refSource: SI },
  קווארט: {
    anchor: 946.352946 / 57.6,
    system: 'volume',
    heDesc: 'quart = 946.4 מ"ל',
    refSource: SI,
  },
  גלון: {
    anchor: 3785.411784 / 57.6,
    system: 'volume',
    heDesc: 'US gallon = 3.785 ל׳',
    refSource: B,
  },
  'UK fl oz': {
    anchor: 28.4130625 / 57.6,
    system: 'volume',
    heDesc: 'UK fluid ounce = 28.41 מ"ל',
    refSource: B,
  },
  'UK פינט': {
    anchor: 568.26125 / 57.6,
    system: 'volume',
    heDesc: 'UK pint = 568.3 מ"ל',
    refSource: B,
  },
  'UK קווארט': {
    anchor: 1136.5225 / 57.6,
    system: 'volume',
    heDesc: 'UK quart = 1.137 ל׳',
    refSource: B,
  },
  'UK גאלון': {
    anchor: 4546.09 / 57.6,
    system: 'volume',
    heDesc: 'UK gallon = 4.546 ל׳',
    refSource: B,
  },
}
