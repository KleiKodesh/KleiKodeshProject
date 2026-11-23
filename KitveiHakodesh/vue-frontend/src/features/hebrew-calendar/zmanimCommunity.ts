// The user's community custom (minhag): which opinion line is primary for each zman group.
// Unset = the custom of the zmanim place's city (e.g. Jerusalem), else none (general).
import { ref, watch } from 'vue'
import { defaultCommunityFor, listCommunities, primaryZmanim, ZMAN_GROUPS, type ZmanName } from 'kleikodesh-zmanim'
import { uiLanguage } from '@/i18n'
import { lsDelete, lsGet, lsSet } from '@/utils/persistence'
import { JERUSALEM_ID } from './zmanimPlaces'

const KEY = 'zmanim.community'
/** Stored value meaning "no community": the general lines. */
const NONE = 'none'

const savedRaw = lsGet<unknown>(KEY)
/** The chosen community id, 'none', or '' = follow the place's custom. */
export const chosenCommunity = ref<string>(typeof savedRaw === 'string' ? savedRaw : '')
/** The place the zmanim are for (useZmanim keeps it current). */
export const communityPlaceId = ref<number>(JERUSALEM_ID)
const primaryNames = ref<readonly ZmanName[]>([])

export function setCommunity(id: string) {
  chosenCommunity.value = id
  if (id) lsSet(KEY, id)
  else lsDelete(KEY)
}

let generation = 0
let started = false

/**
 * Primary lines in group order (empty until loaded). Starts loading on the first call, from a component,
 * so the app has configured the library's asset loader by then.
 */
export function usePrimaryZmanNames() {
  if (!started) { started = true; startWatching() }
  return primaryNames
}

function startWatching() {
  watch([chosenCommunity, communityPlaceId], async ([chosen, placeId]) => {
    const mine = ++generation
    try {
      const community = chosen === NONE ? null : chosen || (await defaultCommunityFor(placeId))
      const groups = await primaryZmanim(community, placeId)
      if (mine !== generation) return
      primaryNames.value = ZMAN_GROUPS.map((g) => groups[g]).filter((n): n is ZmanName => !!n)
    } catch {
      if (mine === generation) primaryNames.value = []
    }
  }, { immediate: true })
}

/** Every community, names in the UI language. */
export async function communityOptions(): Promise<Array<{ id: string, name: string }>> {
  return listCommunities(uiLanguage === 'he' ? 'he' : 'en')
}
