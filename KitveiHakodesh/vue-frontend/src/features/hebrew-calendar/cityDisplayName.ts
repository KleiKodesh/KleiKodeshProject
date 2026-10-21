import { t } from '@/i18n'
import type { City } from './calendarTypes'

/**
 * UI label for a city. Cities in Israel always show their Hebrew name, in every UI
 * language; the rest follow the UI language. `city.name` stays the persisted/lookup value.
 */
export function cityDisplayName(city: City): string {
  if (city.tzid === 'Asia/Jerusalem') return city.name
  return t(`hebrewCalendar.city.${city.key}`)
}
