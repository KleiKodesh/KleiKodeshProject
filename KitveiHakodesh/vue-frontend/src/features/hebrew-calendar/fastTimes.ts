// Fast start / end times. hebcal only says WHICH days are fasts; the times come from our library,
// on the chosen preset's lines: a minor fast runs from alot to tzeit; Tisha B'Av and Yom Kippur start
// at sunset the evening before (Yom Kippur ends with havdalah, shown on its own row).
// Each edge is printed with its own rounding, the stringent side for its use: a start is the end of the
// eating time (alot as the end of the night, or sunset), so it rounds DOWN (earlier) even though the alot
// line of the day rounds up; the end is a nightfall, so it rounds UP (later).
import { formatTime, type Rounding, type ZmanGroup, type ZmanName } from 'kleikodesh-zmanim'
import type { DayTimedZmanim } from './zmanimPlaces'
import { roundSign, type RoundSign } from './zmanRounding'

type PrimaryGroups = Partial<Record<ZmanGroup, ZmanName>>

/** The lines used when the preset names none: the default list's alot / sunset / tzeit lines. */
export const DEFAULT_FAST_LINES = { alot: 'alot90', sunset: 'sunset', tzeit: 'tzeit18Sun' } as const satisfies Record<string, ZmanName>

/** The other common opinion, shown as a second line: alot at 16.05 deg, tzeit at 7.083 deg (three medium stars). */
export const ALT_FAST_LINES = { minorStart: 'alot', end: 'tzeit7083' } as const satisfies Partial<Record<FastEdge, ZmanName>>

/** The second opinion's line and time for a fast edge; null for the sunset start, or when it is the preset's own line. */
export function altFastTime(edge: FastEdge, zmanim: DayTimedZmanim, primary: PrimaryGroups | null, tzid: string): { name: ZmanName, time: string, sign: RoundSign } | null {
  if (!primary || edge === 'majorStart') return null
  const name = ALT_FAST_LINES[edge]
  const time = printEdge(edge, zmanim, name, tzid)
  return name === fastLine(edge, primary) || !time ? null : { name, time, sign: edgeSign(edge, zmanim, name) }
}

export type FastEdge = 'minorStart' | 'majorStart' | 'end'

/** Rounding of each fast edge: the starts are deadlines (round down, earlier), the end a nightfall (round up, later). */
export const FAST_EDGE_ROUNDING: Readonly<Record<FastEdge, Rounding>> = { minorStart: 'down', majorStart: 'down', end: 'up' }

function printEdge(edge: FastEdge, zmanim: DayTimedZmanim, name: ZmanName, tzid: string): string | null {
  const z = zmanim[name]
  return z ? formatTime(z.time.getTime(), FAST_EDGE_ROUNDING[edge], tzid) : null
}

function edgeSign(edge: FastEdge, zmanim: DayTimedZmanim, name: ZmanName): RoundSign {
  const z = zmanim[name]
  return z ? roundSign(z.time.getTime(), FAST_EDGE_ROUNDING[edge]) : null
}

/** Which way a fast edge's printed minute was rounded (null until the preset is loaded). */
export function fastSign(edge: FastEdge, zmanim: DayTimedZmanim, primary: PrimaryGroups | null): RoundSign {
  return primary ? edgeSign(edge, zmanim, fastLine(edge, primary)) : null
}

/** The line a fast edge follows under a preset. */
export function fastLine(edge: FastEdge, primary: PrimaryGroups): ZmanName {
  if (edge === 'minorStart') return primary.alot ?? DEFAULT_FAST_LINES.alot
  if (edge === 'majorStart') return primary.sunset ?? DEFAULT_FAST_LINES.sunset
  return primary.tzeit ?? DEFAULT_FAST_LINES.tzeit
}

/** The printed time of a fast edge on this day (null until the preset is loaded, or where there is no such time). */
export function fastTime(edge: FastEdge, zmanim: DayTimedZmanim, primary: PrimaryGroups | null, tzid: string): string | null {
  return primary ? printEdge(edge, zmanim, fastLine(edge, primary), tzid) : null
}
