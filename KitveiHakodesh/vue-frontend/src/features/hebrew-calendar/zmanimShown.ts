// Which zman lines the zmanim tool shows. By default a short list of the common opinions
// (plus the user's community lines); the user can tick other opinions in the tool's settings
// panel. The choice is saved; "reset" goes back to the default.
import { computed, ref } from 'vue'
import { ZMAN_NAMES, type ZmanName } from 'kleikodesh-zmanim'
import { lsDelete, lsGet, lsSet } from '@/utils/persistence'
import { usePrimaryZmanNames } from './zmanimCommunity'

const KEY = 'zmanim.shown'

/** The common default lines. Names the library doesn't have (yet) are simply skipped. */
export const DEFAULT_SHOWN: readonly string[] = [
  'chatzotNight', 'alot90', 'misheyakir', 'sunriseTerrain', 'sunrise',
  'sofShmaMga16', 'sofShmaGra', 'sofTfillaMga16', 'sofTfillaGra', 'chatzot', 'minchaGedola30',
  'minchaKetana', 'plagAlot90Tzeit645', 'sunset', 'sunsetTerrain', 'tzeit18Sun',
]

/** Candle-lighting lines belong to Fridays and the eve of Yom Tov only. */
export function isCandleLine(name: string): boolean {
  return name.startsWith('candles')
}

/** Opinion groups for the settings panel, in the library's (chronological) order. */
const GROUP_OF: Array<[RegExp, string]> = [
  [/^chatzotNight/, 'chatzotNight'], [/^alot/, 'alot'], [/^misheyakir/, 'misheyakir'], [/^sunrise/, 'sunrise'],
  [/^sofShma/, 'sofShma'], [/^sofTfilla/, 'sofTfilla'], [/^chatzot/, 'chatzot'], [/^minchaGedola/, 'minchaGedola'],
  [/^minchaKetana/, 'minchaKetana'], [/^plag/, 'plag'], [/^candles/, 'candles'], [/^sunset/, 'sunset'],
  [/^beinHashmashot|^bh/, 'beinHashmashot'], [/^tzeitRt/, 'tzeitRt'], [/^tzeit/, 'tzeit'],
]
export function zmanGroup(name: string): string {
  return GROUP_OF.find(([re]) => re.test(name))?.[1] ?? 'other'
}

/** [group, names[]] in library order. */
export const ZMAN_GROUP_LIST: Array<[string, ZmanName[]]> = (() => {
  const map = new Map<string, ZmanName[]>()
  for (const n of ZMAN_NAMES) {
    const g = zmanGroup(n)
    if (!map.has(g)) map.set(g, [])
    map.get(g)!.push(n)
  }
  return [...map]
})()

const saved = lsGet<unknown>(KEY)
/** The user's own selection; null = the default. */
const custom = ref<string[] | null>(Array.isArray(saved) ? saved.filter((x): x is string => typeof x === 'string') : null)

/** The names to show, in library order: the user's selection, or the default plus the community's lines. */
export function useShownZmanNames() {
  const primary = usePrimaryZmanNames()
  return computed<Set<string>>(() => {
    if (custom.value) return new Set(custom.value)
    return new Set([...DEFAULT_SHOWN, ...primary.value])
  })
}

export const isCustomSelection = computed(() => custom.value !== null)

export function setZmanShown(shown: ReadonlySet<string>, name: string, on: boolean) {
  const next = new Set(shown)
  if (on) next.add(name)
  else next.delete(name)
  custom.value = ZMAN_NAMES.filter((n) => next.has(n))
  lsSet(KEY, custom.value)
}

export function resetZmanShown() {
  custom.value = null
  lsDelete(KEY)
}
