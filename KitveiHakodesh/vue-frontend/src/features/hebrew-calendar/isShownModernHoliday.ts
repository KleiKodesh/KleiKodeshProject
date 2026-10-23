import { flags, type Event } from '@hebcal/core'

/** The only modern (state) days shown; all other MODERN_HOLIDAY events are hidden. */
const SHOWN_MODERN_HOLIDAYS = new Set(['Yom HaShoah', 'Yom HaZikaron', "Yom HaAtzma'ut", 'Yom Yerushalayim'])

/** False for modern holidays outside the allow-list; true for every other event. */
export function isShownEvent(e: Event): boolean {
  if (!(e.getFlags() & flags.MODERN_HOLIDAY)) return true
  return SHOWN_MODERN_HOLIDAYS.has(e.basename())
}
