import type { DailyLearning } from './hebrewCalendarLearning'

export interface CalendarZmanim {
  chatzotNight: string | null
  alot: string | null
  alot90: string | null
  misheyakir: string | null
  sunrise: string | null
  sunriseElevated: string | null
  sofShmaGra: string | null
  sofShmaMga: string | null
  sofTfillaGra: string | null
  sofTfillaMga: string | null
  chatzot: string | null
  minchaGedola: string | null
  minchaKetana: string | null
  plag: string | null
  plagMga: string | null
  sunset: string | null
  sunsetElevated: string | null
  tzeitGeonim: string | null
  tzeit: string | null
}

/** Zmanim seen from the place's height; null (and hidden) where the place is not above sea level. */
export const ELEVATED_ZMANIM: ReadonlySet<string> = new Set(['sunriseElevated', 'sunsetElevated'])

export interface CalendarDay {
  date: Date
  dayOfWeek: number // 0=Sun … 6=Sat
  gregDay: number
  hebGem: string // e.g. "כה"
  hebDayName: string // e.g. "שבת"
  isToday: boolean
  isShabbat: boolean
  isFriday: boolean
  // events
  holidays: string[]
  parasha: string | null
  candleLighting: string | null
  havdalah: string | null
  omer: string | null
  chanukahCandles: string | null
  shabbatMevarchim: string | null
  molad: string | null
  yomKippurKatan: string | null
  fastStart: string | null
  fastEnd: string | null
  // learning
  learning: DailyLearning
  // zmanim
  zmanim: CalendarZmanim
}

export interface CalendarWeek {
  hebrewLabel: string
  gregLabel: string
  days: CalendarDay[]
}
