import type { ZmanName } from 'kleikodesh-zmanim'
import type { DailyLearning } from './hebrewCalendarLearning'

/** Every library zman, printed HH:MM; null where the sun never reaches it that day. */
export type CalendarZmanim = Record<ZmanName, string | null>

/** i18n key of a zman's label (both locales; zmanLabels.test.ts checks every library name). */
export const zmanLabelKey = (name: ZmanName): string => `hebrewCalendar.zmanim.${name}`

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
