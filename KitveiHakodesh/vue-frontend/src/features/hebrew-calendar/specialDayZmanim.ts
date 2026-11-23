import { HDate, months } from '@hebcal/hdate'
import { HebrewCalendar } from '@hebcal/core/dist/esm/hebcal'
import { flags } from '@hebcal/core/dist/esm/event'
import {
  epochMs, moladIndexAtOrBefore, moladMs, SPECIAL_KINDS, SPECIAL_ROUNDING, specialZmanInstants,
  formatTime, type SpecialKind, type SpecialZmanName,
} from 'kleikodesh-zmanim'
import type { Place } from './zmanimPlaces'

/** One special-day line: its library name and its printed time (with the date when it falls on another day). */
export interface SpecialZmanRow { key: SpecialZmanName, value: string | null }

/** i18n key of a special zman's label (both locales; zmanLabels.test.ts checks every name). */
export const specialZmanLabelKey = (name: SpecialZmanName): string => `hebrewCalendar.specialZmanim.${name}`

const DAY_MS = 86_400_000

/** The special-day kinds that apply to [date]: erev Pesach, a minor fast (starts at dawn), the kiddush levana window. */
export function specialKindsFor(date: Date, il: boolean): SpecialKind[] {
  const hd = new HDate(date)
  const kinds: SpecialKind[] = []
  if (hd.getMonth() === months.NISAN && hd.getDate() === 14) kinds.push('erevPesach')
  const events = HebrewCalendar.getHolidaysOnDate(hd, il) ?? []
  if (events.some((e) => e.getFlags() & flags.MINOR_FAST)) kinds.push('fastDay')
  // Days (local midnight to midnight) that overlap molad + 3 days .. molad + 15 days.
  const start = new Date(date.getFullYear(), date.getMonth(), date.getDate()).getTime()
  const end = new Date(date.getFullYear(), date.getMonth(), date.getDate() + 1).getTime()
  const m = moladMs(moladIndexAtOrBefore(end))
  if (end > m + 3 * DAY_MS && start < m + 15 * DAY_MS) kinds.push('kiddushLevana')
  return kinds
}

/** The special lines of [date] at [place], in library order per kind. */
export function specialDayRows(place: Place, date: Date): SpecialZmanRow[] {
  const civil = { year: date.getFullYear(), month: date.getMonth() + 1, day: date.getDate() }
  const dayKey = `${civil.year}-${String(civil.month).padStart(2, '0')}-${String(civil.day).padStart(2, '0')}`
  const dm = new Intl.DateTimeFormat('he', { day: 'numeric', month: 'numeric', timeZone: place.tzid })
  const rows: SpecialZmanRow[] = []
  for (const kind of specialKindsFor(date, place.tzid === 'Asia/Jerusalem')) {
    const instants = specialZmanInstants(place, civil, kind)
    for (const key of SPECIAL_KINDS[kind]) {
      const ns = instants[key]
      if (ns == null) { rows.push({ key, value: null }); continue }
      const ms = epochMs(ns)
      const time = formatTime(ms, SPECIAL_ROUNDING[key], place.tzid)
      const sameDay = new Intl.DateTimeFormat('en-CA', { timeZone: place.tzid }).format(ms) === dayKey
      rows.push({ key, value: sameDay ? time : `${dm.format(ms)} ${time}` })
    }
  }
  return rows
}
