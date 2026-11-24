// Fast start / end times. hebcal only says WHICH days are fasts; the times come from our library,
// on the chosen preset's lines: a minor fast runs from alot to tzeit; Tisha B'Av and Yom Kippur start
// at sunset the evening before (Yom Kippur ends with havdalah, shown on its own row).
import type { ZmanGroup, ZmanName } from 'kleikodesh-zmanim'
import type { CalendarZmanim } from './calendarTypes'

type PrimaryGroups = Partial<Record<ZmanGroup, ZmanName>>

/** The lines used when the preset names none: the default list's alot / sunset / tzeit lines. */
export const DEFAULT_FAST_LINES = { alot: 'alot90', sunset: 'sunset', tzeit: 'tzeit18Sun' } as const satisfies Record<string, ZmanName>

/** The other common opinion, shown as a second line: alot at 16.1 deg, tzeit at 7.083 deg (three medium stars). */
export const ALT_FAST_LINES = { minorStart: 'alot', end: 'tzeit7083' } as const satisfies Partial<Record<FastEdge, ZmanName>>

/** The second opinion's line and time for a fast edge; null for the sunset start, or when it is the preset's own line. */
export function altFastTime(edge: FastEdge, zmanim: CalendarZmanim, primary: PrimaryGroups | null): { name: ZmanName, time: string } | null {
  if (!primary || edge === 'majorStart') return null
  const name = ALT_FAST_LINES[edge]
  const time = zmanim[name]
  return name === fastLine(edge, primary) || !time ? null : { name, time }
}

export type FastEdge = 'minorStart' | 'majorStart' | 'end'

/** The line a fast edge follows under a preset. */
export function fastLine(edge: FastEdge, primary: PrimaryGroups): ZmanName {
  if (edge === 'minorStart') return primary.alot ?? DEFAULT_FAST_LINES.alot
  if (edge === 'majorStart') return primary.sunset ?? DEFAULT_FAST_LINES.sunset
  return primary.tzeit ?? DEFAULT_FAST_LINES.tzeit
}

/** The printed time of a fast edge on this day (null until the preset is loaded, or where there is no such time). */
export function fastTime(edge: FastEdge, zmanim: CalendarZmanim, primary: PrimaryGroups | null): string | null {
  return primary ? zmanim[fastLine(edge, primary)] ?? null : null
}
