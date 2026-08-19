import type { DailyLearning } from './hebrewCalendarLearning'

export interface CalendarZmanim {
  chatzotNight: string | null
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

/**
 * The selectable city list. Kept here (pure data, no @hebcal imports) so
 * consumers that only need the coordinates — e.g. the home bar's deferred
 * zmanim loader — can import it without dragging in the hebcal engine.
 */
export const CITIES: City[] = [
  { name: 'ירושלים', lat: 31.7683, lng: 35.2137, elevation: 800, tzid: 'Asia/Jerusalem' },
  { name: 'תל אביב', lat: 32.0853, lng: 34.7818, elevation: 5, tzid: 'Asia/Jerusalem' },
  { name: 'חיפה', lat: 32.794, lng: 34.9896, elevation: 10, tzid: 'Asia/Jerusalem' },
  { name: 'באר שבע', lat: 31.2518, lng: 34.7913, elevation: 280, tzid: 'Asia/Jerusalem' },
  { name: 'אשדוד', lat: 31.8044, lng: 34.6553, elevation: 30, tzid: 'Asia/Jerusalem' },
  { name: 'נתניה', lat: 32.3215, lng: 34.8532, elevation: 20, tzid: 'Asia/Jerusalem' },
  { name: 'פתח תקווה', lat: 32.0878, lng: 34.8878, elevation: 50, tzid: 'Asia/Jerusalem' },
  { name: 'ראשון לציון', lat: 31.9642, lng: 34.8044, elevation: 30, tzid: 'Asia/Jerusalem' },
  { name: 'בני ברק', lat: 32.0833, lng: 34.8333, elevation: 20, tzid: 'Asia/Jerusalem' },
  { name: 'רמת גן', lat: 32.0684, lng: 34.8248, elevation: 30, tzid: 'Asia/Jerusalem' },
  { name: 'הרצליה', lat: 32.1663, lng: 34.8439, elevation: 20, tzid: 'Asia/Jerusalem' },
  { name: 'רחובות', lat: 31.8928, lng: 34.8113, elevation: 50, tzid: 'Asia/Jerusalem' },
  { name: 'מודיעין', lat: 31.8969, lng: 35.0095, elevation: 300, tzid: 'Asia/Jerusalem' },
  { name: 'אילת', lat: 29.5577, lng: 34.9519, elevation: 10, tzid: 'Asia/Jerusalem' },
  { name: 'צפת', lat: 32.9646, lng: 35.4956, elevation: 900, tzid: 'Asia/Jerusalem' },
  { name: 'טבריה', lat: 32.7922, lng: 35.5312, elevation: -210, tzid: 'Asia/Jerusalem' },
  { name: 'ניו יורק', lat: 40.7128, lng: -74.006, elevation: 10, tzid: 'America/New_York' },
  { name: 'לונדון', lat: 51.5074, lng: -0.1278, elevation: 10, tzid: 'Europe/London' },
  { name: 'אנטוורפן', lat: 51.2194, lng: 4.4025, elevation: 10, tzid: 'Europe/Brussels' },
  { name: 'מונטריאול', lat: 45.5017, lng: -73.5673, elevation: 30, tzid: 'America/Toronto' },
]
