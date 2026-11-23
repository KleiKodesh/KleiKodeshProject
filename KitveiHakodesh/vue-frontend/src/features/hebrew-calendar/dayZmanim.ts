import { buildComplexZmanimCalendar } from './kosherZmanim/buildCalendar'
import type { ComplexZmanimCalendar } from './kosherZmanim/calendars/complexZmanimCalendar'
import { ZENITH_11_POINT_5 } from './kosherZmanim/calendars/complexZmanimCalendar'
import { ZENITH_16_POINT_1, ZENITH_8_POINT_5 } from './kosherZmanim/calendars/zmanimCalendar'
import type { Instant } from './kosherZmanim/instant'
import type { CalendarZmanim, City } from './calendarTypes'

/** One row of the day: the instant, and the printed time. */
export interface Zman {
  time: Date
  printed: string
}

/** The app's zmanim rows; null where the sun never gets there. */
export type DayZmanim = Record<keyof CalendarZmanim, Zman | null>

type Rounding = 'up' | 'down' | 'nearest'

/**
 * How each row's printed minute is rounded, the stringent way (halachic convention):
 * - a zman that STARTS something (from when it is allowed / obligatory) rounds UP, so the printed
 *   minute is never before the real moment;
 * - a DEADLINE (until when) rounds DOWN, so the printed minute is never after the real moment;
 * - chatzot is both the end of the morning and the start of the afternoon, so it rounds to nearest.
 * Alot is treated as a start (of the day's mitzvot), not as the end of the night.
 */
const ROUNDING: Record<keyof CalendarZmanim, Rounding> = {
  chatzotNight: 'up',
  alot: 'up',
  misheyakir: 'up',
  sunrise: 'up',
  sofShmaGra: 'down',
  sofShmaMga: 'down',
  sofTfillaGra: 'down',
  sofTfillaMga: 'down',
  chatzot: 'nearest',
  minchaGedola: 'up',
  minchaKetana: 'up',
  plag: 'up',
  sunset: 'down',
  tzeit: 'up',
}

/** Standard, widely published definitions, all from sea-level sunrise/sunset
 * (standard NOAA calculator). */
function rowInstants(today: ComplexZmanimCalendar, yesterday: ComplexZmanimCalendar): Record<keyof CalendarZmanim, Instant | null> {
  return {
    // The midnight that opens this day is yesterday's solar midnight.
    chatzotNight: yesterday.solarMidnight,
    alot: today.getSunriseOffsetByDegrees(ZENITH_16_POINT_1),
    misheyakir: today.getSunriseOffsetByDegrees(ZENITH_11_POINT_5),
    sunrise: today.seaLevelSunrise,
    sofShmaGra: today.sofZmanShmaGRA,
    sofShmaMga: today.sofZmanShmaMGA,
    sofTfillaGra: today.sofZmanTfilaGRA,
    sofTfillaMga: today.sofZmanTfilaMGA,
    chatzot: today.chatzos,
    minchaGedola: today.minchaGedola,
    minchaKetana: today.minchaKetana,
    plag: today.plagHamincha,
    sunset: today.seaLevelSunset,
    tzeit: today.getSunsetOffsetByDegrees(ZENITH_8_POINT_5),
  }
}

function calendarOn(city: City, date: Date): ComplexZmanimCalendar {
  return buildComplexZmanimCalendar({
    lat: city.lat,
    lng: city.lng,
    elevation: city.elevation,
    tzid: city.tzid,
    date: { year: date.getFullYear(), month: date.getMonth() + 1, day: date.getDate() },
    useElevation: false,
  })
}

const MINUTE_MS = 60_000
const formatters = new Map<string, Intl.DateTimeFormat>()

/** HH:MM, two-digit hours (01-24), in the place's time zone. */
function clock(ms: number, timeZone: string): string {
  let format = formatters.get(timeZone)
  if (!format) {
    format = new Intl.DateTimeFormat('he', { hour: '2-digit', minute: '2-digit', hourCycle: 'h24', timeZone })
    formatters.set(timeZone, format)
  }
  return format.formatToParts(ms).map((part) => (part.type === 'dayPeriod' ? '' : part.value.trim())).join('')
}

export function formatZmanTime(time: Date, rounding: Rounding, timeZone: string): string {
  const ms = time.getTime()
  const minutes = ms / MINUTE_MS
  const rounded = rounding === 'up' ? Math.ceil(minutes) : rounding === 'down' ? Math.floor(minutes) : Math.round(minutes)
  return clock(rounded * MINUTE_MS, timeZone)
}

/** The day's zmanim rows for a city. */
export function dayZmanim(city: City, date: Date): DayZmanim {
  const instants = rowInstants(
    calendarOn(city, date),
    calendarOn(city, new Date(date.getFullYear(), date.getMonth(), date.getDate() - 1)),
  )
  const day = {} as DayZmanim
  for (const row of Object.keys(ROUNDING) as Array<keyof CalendarZmanim>) {
    const instant = instants[row]
    const time = instant === null ? null : new Date(instant.toEpochMilliseconds())
    day[row] = time && { time, printed: formatZmanTime(time, ROUNDING[row], city.tzid) }
  }
  return day
}
