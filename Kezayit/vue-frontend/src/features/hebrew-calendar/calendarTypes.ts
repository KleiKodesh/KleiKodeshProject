import type { DailyLearning } from './hebrewCalendarLearning'

export interface CalendarZmanim {
  alot: string | null
  misheyakir: string | null
  sunrise: string | null
  sofShmaGra: string | null
  sofShmaMga: string | null
  sofTfillaGra: string | null
  sofTfillaMga: string | null
  chatzot: string | null
  minchaGedola: string | null
  minchaKetana: string | null
  plag: string | null
  sunset: string | null
  tzeit: string | null
}

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

export interface City {
  name: string
  lat: number
  lng: number
  elevation: number
  tzid: string
}
