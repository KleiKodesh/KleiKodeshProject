// The chosen place's name, saved beside the zmanim.city setting so startup shows it
// without reading the whole place-names list. The list is read only when the name for
// this place and UI language is not saved yet (first run, older settings, new language).
import { lsGet, lsSet } from '@/utils/persistence'
import { uiLanguage } from '@/i18n'

/** Disk name for { id, he?, en? } of the place last shown. */
export const ZMANIM_CITY_NAME_KEY = 'zmanim.cityName'

interface SavedPlaceName { id: number, he?: string, en?: string }

const lang: 'he' | 'en' = uiLanguage === 'he' ? 'he' : 'en'

/** One place's name in the UI language, from the saved copy when it has one. */
export async function savedPlaceName(id: number): Promise<string> {
  const saved = lsGet<SavedPlaceName>(ZMANIM_CITY_NAME_KEY)
  const known = saved?.id === id ? saved[lang] : undefined
  if (known) return known
  const { placeName } = await import('./zmanimPlaces')
  const name = await placeName(id)
  if (name) lsSet<SavedPlaceName>(ZMANIM_CITY_NAME_KEY, { ...(saved?.id === id ? saved : { id }), [lang]: name })
  return name
}
