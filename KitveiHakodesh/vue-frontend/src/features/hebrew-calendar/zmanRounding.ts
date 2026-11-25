// Which way a printed zman minute was rounded (shown as a small sign beside the time).
// Kept apart from zmanimPlaces.ts so it loads without the place data.
import type { Rounding } from 'kleikodesh-zmanim'

/**
 * Which way a printed minute was rounded: 'earlier' (rounded down, the real time is a little after it) or
 * 'later' (rounded up, the real time is a little before it); null when rounded to the nearest minute or exact.
 */
export type RoundSign = 'earlier' | 'later' | null

/** The direction [ms] was rounded by [rounding] when printed HH:MM. */
export function roundSign(ms: number, rounding: Rounding): RoundSign {
  if (rounding === 'nearest' || ms % 60_000 === 0) return null
  return rounding === 'down' ? 'earlier' : 'later'
}
