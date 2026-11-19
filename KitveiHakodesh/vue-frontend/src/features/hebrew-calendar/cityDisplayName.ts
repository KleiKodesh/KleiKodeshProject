import { t, uiLanguage } from '@/i18n'
import type { City } from './calendarTypes'

/** UI label for a city in the current UI language. `city.name` stays the persisted/lookup value. */
export function cityDisplayName(city: City): string {
  return t(`hebrewCalendar.city.${city.key}`)
}

/** Every time zone the city list uses → its country (ISO region code). */
const TZID_COUNTRY: Record<string, string> = {
  'Asia/Jerusalem': 'IL',
  'America/New_York': 'US',
  'America/Chicago': 'US',
  'America/Los_Angeles': 'US',
  'America/Toronto': 'CA',
  'America/Argentina/Buenos_Aires': 'AR',
  'America/Mexico_City': 'MX',
  'America/Sao_Paulo': 'BR',
  'America/Panama': 'PA',
  'Europe/London': 'GB',
  'Europe/Brussels': 'BE',
  'Europe/Kyiv': 'UA',
  'Europe/Madrid': 'ES',
  'Europe/Paris': 'FR',
  'Europe/Minsk': 'BY',
  'Europe/Zurich': 'CH',
  'Africa/Johannesburg': 'ZA',
  'Australia/Darwin': 'AU',
  'Australia/Melbourne': 'AU',
}

let regionNames: Intl.DisplayNames | null = null

/** Country name of a city in the UI language ('' when its time zone is not mapped). */
export function cityCountryName(city: City): string {
  // Itim Labina entry 14 is "the whole world", not a place: no country (sorts first).
  if (city.key === 'itimLabina14') return ''
  const code = TZID_COUNTRY[city.tzid]
  if (!code) return ''
  regionNames ??= new Intl.DisplayNames([uiLanguage], { type: 'region' })
  return regionNames.of(code) ?? code
}

/** "Country - City" label for city pickers. */
export function cityLabelWithCountry(city: City): string {
  const country = cityCountryName(city)
  return country ? `${country} - ${cityDisplayName(city)}` : cityDisplayName(city)
}

/** Cities sorted by country, then by city name. */
export function citiesByCountry(cities: readonly City[]): City[] {
  const collator = new Intl.Collator(uiLanguage)
  return [...cities].sort(
    (a, b) =>
      collator.compare(cityCountryName(a), cityCountryName(b)) ||
      collator.compare(cityDisplayName(a), cityDisplayName(b)),
  )
}
