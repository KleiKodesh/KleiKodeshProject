import type { Unit, UnitSource } from './types'

const M: UnitSource = {
  label: 'המכלול',
  url: 'https://www.hamichlol.org.il/רשימת_מידות,_שיעורים_ומשקלות_בהלכה',
}
const TC: UnitSource = { label: 'TorahCalc', url: 'https://www.torahcalc.com/info/biblical-units/' }
const SI: UnitSource = { label: 'SI' }

const SEC = 22.8
const MIN = SEC * 60
const HOUR = MIN * 60
const DAY = HOUR * 24

export const TIME: Record<string, Unit> = {
  רגע: { anchor: 1, system: 'time', heDesc: '1/76 חלק (≈0.044 שנ׳)', source: M },
  חלק: { anchor: 76, system: 'time', heDesc: '76 רגעים (≈3.33 שנ׳)', source: M },
  עת: { anchor: 142.5, system: 'time', heDesc: '1⅞ חלק', source: TC },
  שעה: { anchor: 82080, system: 'time', heDesc: '1080 חלקים', source: M },
  עונה: { anchor: 82080 * 12, system: 'time', heDesc: '12 שעות (חצי יום)', source: M },
  יום: { anchor: DAY, system: 'time', heDesc: '24 שעות', source: M },
  שבוע: { anchor: DAY * 7, system: 'time', heDesc: '7 ימים', source: M },
  חודש: { anchor: DAY * 29.5, system: 'time', heDesc: '29–30 ימים', source: M },
  תקופה: { anchor: DAY * 91.25, system: 'time', heDesc: 'רבע שנה (≈91 יום)', source: M },
  שנה: { anchor: DAY * 354, system: 'time', heDesc: '354 ימים (שנה לבנה)', source: M },
  שמיטה: { anchor: DAY * 354 * 7, system: 'time', heDesc: '7 שנים', source: M },
  יובל: { anchor: DAY * 354 * 50, system: 'time', heDesc: '50 שנה', source: M },
  שנייה: { anchor: SEC, system: 'time', heDesc: 'שנייה', source: SI },
  דקה: { anchor: MIN, system: 'time', heDesc: '60 שניות', source: SI },
  'שעה (מודרנית)': { anchor: HOUR, system: 'time', heDesc: '60 דקות', source: SI },
  'יום (מודרני)': { anchor: DAY, system: 'time', heDesc: '24 שעות', source: SI },
  שבוע_מ: { anchor: DAY * 7, system: 'time', heDesc: '7 ימים', source: SI },
}
