import { ref, computed, watch } from 'vue'
import { Zmanim, GeoLocation } from '@hebcal/core'
import { idbGet, idbSet, KEYS } from '@/utils/idbPersistence'

export interface City {
  name: string
  lat: number
  lng: number
  elevation: number
  tzid: string
}

export const CITIES: City[] = [
  { name: 'ירושלים', lat: 31.7683, lng: 35.2137, elevation: 800, tzid: 'Asia/Jerusalem' },
  { name: 'תל אביב', lat: 32.0853, lng: 34.7818, elevation: 5, tzid: 'Asia/Jerusalem' },
  { name: 'חיפה', lat: 32.794, lng: 34.9896, elevation: 10, tzid: 'Asia/Jerusalem' },
  { name: 'באר שבע', lat: 31.2518, lng: 34.7913, elevation: 280, tzid: 'Asia/Jerusalem' },
  { name: 'אשדוד', lat: 31.8044, lng: 34.6553, elevation: 30, tzid: 'Asia/Jerusalem' },
  { name: 'נתניה', lat: 32.3215, lng: 34.8532, elevation: 20, tzid: 'Asia/Jerusalem' },
  { name: 'פתח תקווה', lat: 32.0878, lng: 34.8878, elevation: 50, tzid: 'Asia/Jerusalem' },
  { name: 'ראשון לציון', lat: 31.9642, lng: 34.8044, elevation: 30, tzid: 'Asia/Jerusalem' },
  { name: 'בני ברק', lat: 32.0833, lng: 34.8333, elevation: 20, tzid: 'Asia/Jerusalem' },
  { name: 'רמת גן', lat: 32.0684, lng: 34.8248, elevation: 30, tzid: 'Asia/Jerusalem' },
  { name: 'הרצליה', lat: 32.1663, lng: 34.8439, elevation: 20, tzid: 'Asia/Jerusalem' },
  { name: 'רחובות', lat: 31.8928, lng: 34.8113, elevation: 50, tzid: 'Asia/Jerusalem' },
  { name: 'מודיעין', lat: 31.8969, lng: 35.0095, elevation: 300, tzid: 'Asia/Jerusalem' },
  { name: 'אילת', lat: 29.5577, lng: 34.9519, elevation: 10, tzid: 'Asia/Jerusalem' },
  { name: 'צפת', lat: 32.9646, lng: 35.4956, elevation: 900, tzid: 'Asia/Jerusalem' },
  { name: 'טבריה', lat: 32.7922, lng: 35.5312, elevation: -210, tzid: 'Asia/Jerusalem' },
  { name: 'ניו יורק', lat: 40.7128, lng: -74.006, elevation: 10, tzid: 'America/New_York' },
  { name: 'לונדון', lat: 51.5074, lng: -0.1278, elevation: 10, tzid: 'Europe/London' },
  { name: 'אנטוורפן', lat: 51.2194, lng: 4.4025, elevation: 10, tzid: 'Europe/Brussels' },
  { name: 'מונטריאול', lat: 45.5017, lng: -73.5673, elevation: 30, tzid: 'America/Toronto' },
]

const JERUSALEM = CITIES[0]!

export interface ZmanimEntry {
  label: string
  time: Date | null
}

function zdtToDate(zdt: { epochMilliseconds: number } | null): Date | null {
  if (!zdt) return null
  return new Date(zdt.epochMilliseconds)
}

function calcZmanim(city: City, date: Date): ZmanimEntry[] {
  try {
    const gloc = new GeoLocation(city.name, city.lat, city.lng, city.elevation, city.tzid)
    const z = new Zmanim(gloc, date, false)
    return [
      { label: 'עלות השחר (72 דק׳)', time: zdtToDate(z.alotHaShachar72zdt()) },
      { label: 'עלות השחר', time: z.alotHaShachar() },
      { label: 'משיכיר', time: z.misheyakir() },
      { label: 'הנץ החמה', time: z.sunrise() },
      { label: 'סוף זמן ק״ש (גר״א)', time: z.sofZmanShma() },
      { label: 'סוף זמן ק״ש (מג״א)', time: z.sofZmanShmaMGA() },
      { label: 'סוף זמן תפילה (גר״א)', time: z.sofZmanTfilla() },
      { label: 'סוף זמן תפילה (מג״א)', time: z.sofZmanTfillaMGA() },
      { label: 'חצות', time: z.chatzot() },
      { label: 'מנחה גדולה', time: z.minchaGedola() },
      { label: 'מנחה קטנה', time: z.minchaKetana() },
      { label: 'פלג המנחה', time: z.plagHaMincha() },
      { label: 'שקיעה', time: z.sunset() },
      { label: 'צאת הכוכבים', time: z.tzeit() },
      { label: 'צאת הכוכבים (72 דק׳)', time: zdtToDate(z.tzeit72()) },
    ]
  } catch {
    return []
  }
}

function formatTime(date: Date | null): string {
  if (!date || isNaN(date.getTime())) return '—'
  return date.toLocaleTimeString('he-IL', { hour: '2-digit', minute: '2-digit', hour12: false })
}

export function useZmanim() {
  const selectedCity = ref<City | null>(null) // null = using geolocation
  const geoCity = ref<City | null>(null) // resolved from geolocation
  const locationStatus = ref<'loading' | 'geo' | 'manual' | 'fallback'>('loading')

  const activeCity = computed(() => selectedCity.value ?? geoCity.value ?? JERUSALEM)

  const today = new Date()
  today.setHours(0, 0, 0, 0)

  const entries = computed(() => calcZmanim(activeCity.value, today))

  const formattedEntries = computed(() =>
    entries.value.map((e) => ({ label: e.label, time: formatTime(e.time) })),
  )

  // Persist manual city selection
  watch(selectedCity, (city) => {
    idbSet(KEYS.SETTINGS_ZMANIM_CITY, city ? city.name : null)
  })

  async function init() {
    // Restore saved city preference
    const savedName = await idbGet<string | null>(KEYS.SETTINGS_ZMANIM_CITY)
    if (savedName) {
      const found = CITIES.find((c) => c.name === savedName)
      if (found) {
        selectedCity.value = found
        locationStatus.value = 'manual'
        return
      }
    }

    // Try geolocation
    if (!navigator.geolocation) {
      locationStatus.value = 'fallback'
      return
    }

    navigator.geolocation.getCurrentPosition(
      (pos) => {
        // Find nearest city by straight-line distance
        const { latitude, longitude } = pos.coords
        let nearest = JERUSALEM
        let minDist = Infinity
        for (const city of CITIES) {
          const d =
            (city.lat - latitude) * (city.lat - latitude) +
            (city.lng - longitude) * (city.lng - longitude)
          if (d < minDist) {
            minDist = d
            nearest = city
          }
        }
        geoCity.value = nearest
        locationStatus.value = 'geo'
      },
      () => {
        // Permission denied or error — use Jerusalem fallback
        locationStatus.value = 'fallback'
      },
      { timeout: 8000, maximumAge: 60 * 60 * 1000 },
    )
  }

  function setCity(city: City | null) {
    selectedCity.value = city
    locationStatus.value = city ? 'manual' : locationStatus.value === 'geo' ? 'geo' : 'fallback'
  }

  return {
    activeCity,
    selectedCity,
    locationStatus,
    formattedEntries,
    cities: CITIES,
    init,
    setCity,
  }
}
