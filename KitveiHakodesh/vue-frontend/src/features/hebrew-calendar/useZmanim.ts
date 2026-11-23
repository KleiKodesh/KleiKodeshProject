import { ref, shallowRef, watch } from 'vue'
import { lsGet, lsSet } from '@/utils/persistence'
import { communityPlaceId } from './zmanimCommunity'
import { loadPlace, nearestPlace, placeName, type Place } from './zmanimPlaces'

/**
 * Disk name for the selected zmanim place (its numeric place id). Exported because the
 * home page's next-zman bar reads the same preference — this feature owns it, `home` consumes it.
 */
export const ZMANIM_CITY_KEY = 'zmanim.city'
const KEYS = { SETTINGS_ZMANIM_CITY: ZMANIM_CITY_KEY } as const

/** The saved place id, or undefined when none (or an old saved name) is stored. */
export function savedPlaceId(): number | undefined {
  const saved = lsGet<unknown>(KEYS.SETTINGS_ZMANIM_CITY)
  return typeof saved === 'number' ? saved : undefined
}

export function useZmanim() {
  /** The place the zmanim are for; null until it has loaded. */
  const activeCity = shallowRef<Place | null>(null)
  /** Its name in the UI language ('' until loaded). */
  const activeCityName = ref('')
  const status = ref<'loading' | 'geo' | 'manual' | 'fallback'>('loading')
  let pickedByUser = false

  watch(activeCity, async (place) => {
    if (place) communityPlaceId.value = place.id
    activeCityName.value = place ? await placeName(place.id) : ''
  })

  async function init() {
    const saved = savedPlaceId()
    if (saved !== undefined) {
      const place = await loadPlace(saved)
      if (pickedByUser) return
      activeCity.value = place
      status.value = place.id === saved ? 'manual' : 'fallback'
      if (place.id === saved) return
    } else {
      activeCity.value = await loadPlace(undefined)
    }
    if (!navigator.geolocation) {
      status.value = 'fallback'
      return
    }
    navigator.geolocation.getCurrentPosition(
      async ({ coords }) => {
        const place = await nearestPlace(coords.latitude, coords.longitude)
        // A place picked while the lookup was pending wins; don't overwrite it.
        if (pickedByUser) return
        activeCity.value = place
        status.value = 'geo'
        lsSet(KEYS.SETTINGS_ZMANIM_CITY, place.id)
      },
      () => { if (!pickedByUser) status.value = 'fallback' },
      { timeout: 8000, maximumAge: 3_600_000 },
    )
  }

  async function setCity(id: number) {
    pickedByUser = true
    lsSet(KEYS.SETTINGS_ZMANIM_CITY, id)
    activeCity.value = await loadPlace(id)
    status.value = 'manual'
  }

  return { activeCity, activeCityName, status, init, setCity }
}
