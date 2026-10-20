import { ref, computed, watch } from 'vue'
import { lsGet, lsSet } from '@/utils/persistence'
import { itimLabinaDayZmanim, type ItimLabinaZman } from './itimLabinaZmanim'

/**
 * Disk name for the selected zmanim city. Exported because the home page's next-zman
 * bar reads the same preference — this feature owns it, `home` consumes it.
 */
export const ZMANIM_CITY_KEY = 'zmanim.city'
const KEYS = { SETTINGS_ZMANIM_CITY: ZMANIM_CITY_KEY } as const
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

function printed(zman: ItimLabinaZman | null): string | null {
  return zman?.printed ?? null
}

export function calcDayZmanim(city: City, date: Date) {
  try {
    const z = itimLabinaDayZmanim(city, date)
    return {
      chatzotNight: printed(z.chatzotNight),
      alot: printed(z.alot),
      misheyakir: printed(z.misheyakir),
      sunrise: printed(z.sunrise),
      sofShmaGra: printed(z.sofShmaGra),
      sofShmaMga: printed(z.sofShmaMga),
      sofTfillaGra: printed(z.sofTfillaGra),
      sofTfillaMga: printed(z.sofTfillaMga),
      chatzot: printed(z.chatzot),
      minchaGedola: printed(z.minchaGedola),
      minchaKetana: printed(z.minchaKetana),
      plag: printed(z.plag),
      sunset: printed(z.sunset),
      tzeit: printed(z.tzeit),
    }
  } catch {
    return {
      chatzotNight: null,
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
        // A city picked while the lookup was pending wins; don't overwrite it.
        if (manualCity.value) return
        status.value = 'geo'
        lsSet(KEYS.SETTINGS_ZMANIM_CITY, geoCity.value.name)
      },
      () => { if (!manualCity.value) status.value = 'fallback' },
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
