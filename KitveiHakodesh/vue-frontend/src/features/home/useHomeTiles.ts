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
import {
  useFrequentFoldersStore,
  type FrequentFolderEntry,
} from '@/stores/frequentFoldersStore'

const TILE_WIDTH = 72
const TILE_GAP = 8

/** Hard ceiling on recently-opened tiles, regardless of available row space. */
const RECENTLY_OPENED_MAX = 20

/** Hard ceiling on frequently-visited folder tiles. */
const FREQUENT_FOLDERS_TILE_MAX = 4


/**
 * The home tile grid: the static navigation tiles, then the frequently-visited
 * folder tiles, then however many recently-opened tiles fit alongside them.
 *
 * The first two tiles are DB-dependent and swap when no database is available.
 * Every other tile is always shown — see the feature README before changing this.
 *
 * The static tile list must stay in sync with the destination list in
 * `AppTitleBarNavDropdown.vue`; neither is derived from the other.
 */
export function useHomeTiles(containerWidth: Ref<number>) {
  const recentlyOpenedStore = useRecentlyOpenedStore()
  const frequentFoldersStore = useFrequentFoldersStore()
  const { showRecentlyOpened, showFrequentFolders } = storeToRefs(useSettingsStore())

  const recentlyOpenedList = ref<RecentlyOpenedEntry[]>([])
  const frequentFolderList = ref<FrequentFolderEntry[]>([])

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
        : { label: 'קטלוג הספרים', ...tileIcon('library') },
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

  /**
   * How wide the grid is allowed to get: exactly the width of one full row of
   * static tiles. The static row is the widest the grid ever needs to be, so
   * capping here means a wide window can never start a row longer than that —
   * the recently-opened tiles wrap underneath instead of stretching past it.
   */
  const gridMaxWidth = computed(
    () => tiles.value.length * (TILE_WIDTH + TILE_GAP) - TILE_GAP,
  )

  /**
   * How many tiles sit on one row at the current width. Clamped to the grid's own
   * cap, not just the container: past that width the grid stops growing, so extra
   * container width buys no extra columns.
   */
  const tilesPerRow = computed(() => {
    const effectiveWidth = Math.min(containerWidth.value || 320, gridMaxWidth.value)
    return Math.max(1, Math.floor((effectiveWidth + TILE_GAP) / (TILE_WIDTH + TILE_GAP)))
  })

  /**
   * The folder tiles, shown between the static tiles and the recents.
   *
   * These take their slots first — they are the smaller, fixed-size group, and
   * letting the recents claim the row would push them off screen entirely on a
   * narrow window.
   */
  const visibleFrequentFolderList = computed(() => {
    if (!showFrequentFolders.value) return []
    return frequentFolderList.value.slice(0, FREQUENT_FOLDERS_TILE_MAX)
  })

  // Fill the gap left on the last row of everything above, plus one more full row.
  const visibleRecentlyOpenedList = computed(() => {
    if (!showRecentlyOpened.value) return []
    if (!recentlyOpenedList.value.length) return []
    const perRow = tilesPerRow.value
    // The folder tiles sit in the same flow, so the gap the recents are filling is
    // the one left after them — not after the static tiles alone.
    const precedingCount = tiles.value.length + visibleFrequentFolderList.value.length
    const tailSlots = precedingCount % perRow
    const freeOnLastRow = tailSlots === 0 ? 0 : perRow - tailSlots
    const count = Math.min(RECENTLY_OPENED_MAX, freeOnLastRow + perRow)
    return recentlyOpenedList.value.slice(0, count)
  })

  const totalTileCount = computed(
    () =>
      tiles.value.length +
      visibleFrequentFolderList.value.length +
      visibleRecentlyOpenedList.value.length,
  )

  function getRecentTileIcon(entry: RecentlyOpenedEntry): { icon: Component; color: string } {
    // The one shared table — see utils/documentIcons.
    const icon = documentIcon(iconKeyForRoute(entry.route, entry.isOtzariaAddin))
    return { icon: icon.icon24, color: icon.color }
  }

  /** Every folder tile shows the same folder glyph — see utils/documentIcons. */
  function getFolderTileIcon(): { icon: Component; color: string } {
    return tileIcon('folder')
  }

  function onTogglePinFolder(entry: FrequentFolderEntry) {
    frequentFolderList.value = frequentFoldersStore.togglePin(entry.path)
  }

  function onRemoveFolder(entry: FrequentFolderEntry) {
    frequentFolderList.value = frequentFoldersStore.removeEntry(entry.path)
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
    frequentFoldersStore.getList().then((list) => {
      frequentFolderList.value = list
    })
  })

  return {
    tiles,
    gridMaxWidth,
    visibleFrequentFolderList,
    visibleRecentlyOpenedList,
    totalTileCount,
    getFolderTileIcon,
    getRecentTileIcon,
    onTogglePinFolder,
    onRemoveFolder,
    onTogglePinRecent,
    onRemoveRecent,
  }
}
