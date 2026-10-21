import { t } from '@/i18n'
import type { City } from './calendarTypes'

/** UI label for a city in the current UI language. `city.name` stays the persisted/lookup value. */
export function cityDisplayName(city: City): string {
  return t(`hebrewCalendar.city.${city.key}`)
}
