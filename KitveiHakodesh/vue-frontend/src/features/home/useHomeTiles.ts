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
/** How many full rows the folder and document tiles get, on top of the static row's tail. */
const DYNAMIC_TILE_ROWS = 5


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
   * How many tiles the two dynamic groups get between them: the gap left on the
   * static tiles' last row, plus DYNAMIC_TILE_ROWS more full rows.
   */
  const dynamicTileBudget = computed(() => {
    const perRow = tilesPerRow.value
    const tailSlots = tiles.value.length % perRow
    const freeOnLastRow = tailSlots === 0 ? 0 : perRow - tailSlots
    return freeOnLastRow + DYNAMIC_TILE_ROWS * perRow
  })

  /**
   * The folder tiles, shown between the static tiles and the documents.
   *
   * Folders get half the budget. They earn their points from the same opens the
   * documents do, so an unsplit budget would let a folder opened all morning
   * push the documents off the page entirely — and the two are not substitutes
   * for each other.
   *
   * Rounded down, so an odd budget gives the spare slot to the documents. The
   * half-share is a ceiling on the folders rather than a reservation, so with
   * the documents switched off the folders take the whole budget instead of
   * leaving half the row blank.
   */
  const visibleFrequentFolderList = computed(() => {
    if (!showFrequentFolders.value) return []
    const budget = dynamicTileBudget.value
    const room = showRecentlyOpened.value ? Math.floor(budget / 2) : budget
    return frequentFolderList.value.slice(0, room)
  })

  /**
   * The document tiles. They take the rest of the budget, including whatever the
   * folders did not claim — a half-share is a ceiling on the folders, not a
   * reservation held empty on their behalf.
   */
  const visibleRecentlyOpenedList = computed(() => {
    if (!showRecentlyOpened.value) return []
    if (!recentlyOpenedList.value.length) return []
    const room = dynamicTileBudget.value - visibleFrequentFolderList.value.length
    return recentlyOpenedList.value.slice(0, Math.max(0, room))
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
