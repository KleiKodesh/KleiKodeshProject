import { ref, computed, onMounted, type Component, type Ref } from 'vue'
import { storeToRefs } from 'pinia'
import {
  IconDatabase24Filled,
  IconArrowDownload24Filled,
} from '@iconify-prerendered/vue-fluent'
import { documentIcon, iconKeyForRoute, type DocumentIconKey } from '@/utils/documentIcons'
import { dbReady } from '@/webview-host/seforimDb'
import { useSettingsStore } from '@/stores/settingsStore'
import {
  useRecentlyOpenedStore,
  type RecentlyOpenedEntry,
} from '@/stores/recentlyOpenedStore'

const TILE_WIDTH = 72
const TILE_GAP = 16

/** Hard ceiling on recently-opened tiles, regardless of available row space. */
const RECENTLY_OPENED_MAX = 20


/**
 * The home tile grid: the static navigation tiles plus however many
 * recently-opened tiles fit alongside them.
 *
 * The first two tiles are DB-dependent and swap when no database is available.
 * Every other tile is always shown — see the feature README before changing this.
 *
 * The static tile list must stay in sync with the destination list in
 * `AppTitleBarNavDropdown.vue`; neither is derived from the other.
 */
export function useHomeTiles(containerWidth: Ref<number>) {
  const recentlyOpenedStore = useRecentlyOpenedStore()
  const { showRecentlyOpened } = storeToRefs(useSettingsStore())

  const recentlyOpenedList = ref<RecentlyOpenedEntry[]>([])

  /** A static tile's icon + colour, taken from the shared table. */
  function tileIcon(key: DocumentIconKey): { icon: Component; color: string } {
    const icon = documentIcon(key)
    return { icon: icon.icon24, color: icon.color }
  }

  const tiles = computed(() => {
    const dbMissing = !dbReady.value
    return [
      // Tiles whose destination also appears as a tab read their glyph and colour
      // from the shared table (utils/documentIcons), so the tile, the dropdown row
      // and the native tab strip all show the same thing. The rest are tile-only.
      dbMissing
        ? { label: 'הורד מסד ספרים', icon: IconArrowDownload24Filled, color: '#B5451B' }
        : { label: 'ספרים', ...tileIcon('library') },
      dbMissing
        ? { label: 'בחר מסד ספרים', icon: IconDatabase24Filled, color: '#3478f6' }
        : { label: 'חיפוש', ...tileIcon('search') },
      { label: 'היברו-בוקס', ...tileIcon('hbooks') },
      { label: 'פתח קובץ', ...tileIcon('folder') },
      { label: 'חיפוש קבצים', ...tileIcon('fileSearch'), iconScale: 0.93 },
      { label: 'מילון', ...tileIcon('dict') },
      { label: 'לוח שנה', ...tileIcon('calendar') },
      { label: 'מידות ושיעורים', ...tileIcon('ruler') },
      { label: 'סביבות עבודה', ...tileIcon('apps') },
      { label: 'הגדרות', ...tileIcon('settings') },
    ]
  })

  // Fill the gap left on the static tiles' last row, plus one more full row.
  const visibleRecentlyOpenedList = computed(() => {
    if (!showRecentlyOpened.value) return []
    if (!recentlyOpenedList.value.length) return []
    const effectiveWidth = containerWidth.value || 320
    const tilesPerRow = Math.max(
      1,
      Math.floor((effectiveWidth + TILE_GAP) / (TILE_WIDTH + TILE_GAP)),
    )
    const staticTailSlots = tiles.value.length % tilesPerRow
    const freeOnLastRow = staticTailSlots === 0 ? 0 : tilesPerRow - staticTailSlots
    const count = Math.min(RECENTLY_OPENED_MAX, freeOnLastRow + tilesPerRow)
    return recentlyOpenedList.value.slice(0, count)
  })

  const totalTileCount = computed(() => tiles.value.length + visibleRecentlyOpenedList.value.length)

  function getRecentTileIcon(entry: RecentlyOpenedEntry): { icon: Component; color: string } {
    // The one shared table — see utils/documentIcons.
    const icon = documentIcon(iconKeyForRoute(entry.route, entry.isOtzariaAddin))
    return { icon: icon.icon24, color: icon.color }
  }

  function onTogglePinRecent(entry: RecentlyOpenedEntry) {
    recentlyOpenedList.value = recentlyOpenedStore.togglePin(entry.key)
  }

  function onRemoveRecent(entry: RecentlyOpenedEntry) {
    recentlyOpenedList.value = recentlyOpenedStore.removeEntry(entry.key)
  }

  onMounted(() => {
    // Deliberately not awaited by the caller's focus logic — on a cold start this
    // first-run IndexedDB read is slow and must not delay focusing the search input.
    recentlyOpenedStore.getList().then((list) => {
      recentlyOpenedList.value = list
    })
  })

  return {
    tiles,
    visibleRecentlyOpenedList,
    totalTileCount,
    getRecentTileIcon,
    onTogglePinRecent,
    onRemoveRecent,
  }
}
