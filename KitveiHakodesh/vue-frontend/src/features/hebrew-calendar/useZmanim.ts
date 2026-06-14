import { ref, computed, watch } from 'vue'
import { GeoLocation } from '@hebcal/noaa'
import { Zmanim } from '@hebcal/core/dist/esm/zmanim'
import { lsGet, lsSet, KEYS } from '@/utils/persistence'
import type { City } from './calendarTypes'

export type { City }

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

function nearestCity(lat: number, lng: number): City {
  let best = JERUSALEM
  let bestDist = Infinity
  for (const c of CITIES) {
    const d = (c.lat - lat) ** 2 + (c.lng - lng) ** 2
    if (d < bestDist) { bestDist = d; best = c }
  }
  return best
}

export function makeGloc(city: City): InstanceType<typeof GeoLocation> {
  return new GeoLocation(city.name, city.lat, city.lng, city.elevation, city.tzid)
}

export function fmtTime(d: Date | null): string | null {
  if (!d || isNaN(d.getTime())) return null
  return d.toLocaleTimeString('he-IL', { hour: '2-digit', minute: '2-digit', hour12: false })
}

export function calcDayZmanim(city: City, date: Date) {
  try {
    const z = new Zmanim(makeGloc(city), date, false)
    return {
      alot: fmtTime(z.alotHaShachar()),
      misheyakir: fmtTime(z.misheyakir()),
      sunrise: fmtTime(z.sunrise()),
      sofShmaGra: fmtTime(z.sofZmanShma()),
      sofShmaMga: fmtTime(z.sofZmanShmaMGA()),
      sofTfillaGra: fmtTime(z.sofZmanTfilla()),
      sofTfillaMga: fmtTime(z.sofZmanTfillaMGA()),
      chatzot: fmtTime(z.chatzot()),
      minchaGedola: fmtTime(z.minchaGedola()),
      minchaKetana: fmtTime(z.minchaKetana()),
      plag: fmtTime(z.plagHaMincha()),
      sunset: fmtTime(z.sunset()),
      tzeit: fmtTime(z.tzeit()),
    }
  } catch {
    return {
      alot: null, misheyakir: null, sunrise: null, sofShmaGra: null, sofShmaMga: null,
      sofTfillaGra: null, sofTfillaMga: null, chatzot: null, minchaGedola: null,
      minchaKetana: null, plag: null, sunset: null, tzeit: null,
    }
  }
}

export function useZmanim() {
  const manualCity = ref<City | null>(null)
  const geoCity = ref<City | null>(null)
  const status = ref<'loading' | 'geo' | 'manual' | 'fallback'>('loading')

  const activeCity = computed(() => manualCity.value ?? geoCity.value ?? JERUSALEM)

  watch(manualCity, (c) => lsSet(KEYS.SETTINGS_ZMANIM_CITY, c?.name ?? null))

  async function init(preloadedCity?: string) {
    const saved = preloadedCity ?? lsGet<string>(KEYS.SETTINGS_ZMANIM_CITY)
    if (saved) {
      const found = CITIES.find((c) => c.name === saved)
      if (found) {
        manualCity.value = found
        status.value = 'manual'
        return
      }
    }
    if (!navigator.geolocation) {
      status.value = 'fallback'
      return
    }
    navigator.geolocation.getCurrentPosition(
      ({ coords }) => {
        geoCity.value = nearestCity(coords.latitude, coords.longitude)
        status.value = 'geo'
        lsSet(KEYS.SETTINGS_ZMANIM_CITY, geoCity.value.name)
      },
      () => { status.value = 'fallback' },
      { timeout: 8000, maximumAge: 3_600_000 },
    )
  }

  function setCity(city: City | null) {
    manualCity.value = city
    if (!city) status.value = geoCity.value ? 'geo' : 'fallback'
    else status.value = 'manual'
  }

  return { activeCity, manualCity, status, cities: CITIES, init, setCity }
}
