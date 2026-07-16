import { ref, computed, watch } from 'vue'
import { GeoLocation } from '@hebcal/noaa'
import { Zmanim } from '@hebcal/core/dist/esm/zmanim'
import { lsGet, lsSet, KEYS } from '@/utils/persistence'
import type { City } from './calendarTypes'
import { CITIES } from './calendarTypes'

export type { City }
export { CITIES }

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
