// The app's door to the kleikodesh-zmanim library (submodule ../kleikodesh-zmanim.js):
// places by numeric id, names in the UI language, and the day's zmanim.
// The data files ship beside the app in zmanim/ (see zmanimAssets in vite.config.ts) and
// are read on first need: names only when a picker opens, the coordinates when zmanim
// are first computed.
import {
  configure, epochMs, formatTime, getPlace, listPlaces, placeCount, ROUNDING, searchPlaces,
  toCivilDate, ZMAN_NAMES, zmanInstants, type Lang, type Place, type ZmanName,
} from 'kleikodesh-zmanim'
import { uiLanguage } from '@/i18n'
import type { CalendarZmanim } from './calendarTypes'

export type { Place }

configure({ baseUrl: new URL(`${import.meta.env.BASE_URL}zmanim/`, document.baseURI) })

/** The default place. */
export const JERUSALEM_ID = 1

const lang: Lang = uiLanguage === 'he' ? 'he' : 'en'

/** Every place name in the UI language; index = place id (index 0 is empty). */
export function placeNames(): Promise<readonly string[]> {
  return listPlaces(lang)
}

/** One place's name in the UI language. */
export async function placeName(id: number): Promise<string> {
  return (await placeNames())[id] ?? ''
}

/** Places whose name contains [query], in the UI language. */
export function findPlaces(query: string): Promise<Array<{ id: number, name: string }>> {
  return searchPlaces(query, lang, Number.MAX_SAFE_INTEGER)
}

/** The saved place, or Jerusalem when nothing valid is saved (older versions saved a name). */
export async function loadPlace(saved: unknown): Promise<Place> {
  if (typeof saved === 'number' && Number.isInteger(saved) && saved > 0) {
    try {
      return await getPlace(saved)
    } catch {
      // An id this place list does not have: fall back below.
    }
  }
  return getPlace(JERUSALEM_ID)
}

/** The listed place nearest to a point. */
export async function nearestPlace(lat: number, lng: number): Promise<Place> {
  let best = await getPlace(JERUSALEM_ID)
  let bestDistance = Infinity
  const count = await placeCount()
  for (let id = 1; id <= count; id++) {
    const place = await getPlace(id).catch(() => null)
    if (!place) continue
    const distance = (place.lat - lat) ** 2 + (place.lng - lng) ** 2
    if (distance < bestDistance) { bestDistance = distance; best = place }
  }
  return best
}

/** One zman: its instant and its printed HH:MM. */
export interface TimedZman {
  time: Date
  printed: string
}

export type DayTimedZmanim = Record<ZmanName, TimedZman | null>

/** The day's zmanim with their instants (null where the sun never gets there, or on error). */
export function dayTimedZmanim(place: Place, date: Date): DayTimedZmanim {
  const day = {} as DayTimedZmanim
  let instants: Partial<Record<ZmanName, bigint | null>> = {}
  try {
    instants = zmanInstants(place, toCivilDate(date))
  } catch {
    // Leave every row empty.
  }
  for (const name of ZMAN_NAMES) {
    const ns = instants[name]
    if (ns == null) { day[name] = null; continue }
    const ms = epochMs(ns)
    day[name] = { time: new Date(ms), printed: formatTime(ms, ROUNDING[name], place.tzid) }
  }
  return day
}

/** The day's printed zmanim, as the calendar shows them. */
export function dayPrintedZmanim(place: Place, date: Date): CalendarZmanim {
  const day = dayTimedZmanim(place, date)
  return Object.fromEntries(ZMAN_NAMES.map((name) => [name, day[name]?.printed ?? null])) as unknown as CalendarZmanim
}
