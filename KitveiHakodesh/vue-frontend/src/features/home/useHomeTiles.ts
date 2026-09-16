import { ref, computed, inject, onMounted, type Component, type Ref } from 'vue'
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
/** The grid's COLUMN gap (`.home-grid` uses `gap: 4px 8px`) — the axis tile-row
 *  arithmetic wraps along. Keep in step with that rule. */
const TILE_GAP = 8
/**
 * The most folder and document tiles ever shown between them.
 *
 * A flat count, deliberately NOT derived from how many tiles fit on a row. When
 * it was, a nine-wide window showed 45 dynamic tiles and a narrow one showed ~10
 * — same user, same work. These lists are a shortlist of what you are actually
 * working in, and that does not change when you resize the window. Width still
 * decides how the tiles WRAP, never how many of them there are.
 */
const MAX_DYNAMIC_TILES = 18
/** The most folder tiles ever shown, however much budget there is to go round. */
const MAX_FOLDER_TILES = 6


/**
 * The home tile grid: the static navigation tiles, then the frequently-visited
 * folder tiles, then however many recently-opened tiles fit alongside them.
 *
 * The first two tiles are DB-dependent and swap when no database is available.
 * The static row as a whole drops out when this pane's nav rail is showing — see
 * `staticTilesVisible` below and the feature README before changing this.
 *
 * The static tile list must stay in sync with the destination list in
 * `AppTitleBarNavDropdown.vue`; neither is derived from the other.
 */
export function useHomeTiles(containerWidth: Ref<number>) {
  const recentlyOpenedStore = useRecentlyOpenedStore()
  const frequentFoldersStore = useFrequentFoldersStore()
  const settingsStore = useSettingsStore()
  const { showRecentlyOpened, showFrequentFolders } = storeToRefs(settingsStore)
  // Which pane this home page is in — the rail is per-pane, so the tiles follow
  // the rail beside THEM, not the one in the other half of a split view.
  const paneId = inject<1 | 2>('paneId', 1)

  const recentlyOpenedList = ref<RecentlyOpenedEntry[]>([])
  const frequentFolderList = ref<FrequentFolderEntry[]>([])

  /** A static tile's icon + colour, taken from the shared table. */
  function tileIcon(key: DocumentIconKey): { icon: Component; color: string } {
    const icon = documentIcon(key)
    return { icon: icon.icon24, color: icon.color }
  }

  /**
   * The static tiles are the same destinations the nav rail holds, so when the rail
   * is beside this pane they are pure duplication — the rail offers every one of
   * them on every page, not just this one. Dropping them leaves home as what the
   * rail cannot show: the search bar and the user's own folders and documents.
   * Turning the rail off brings them straight back.
   */
  const staticTilesVisible = computed(() => !settingsStore.getNavSidebarVisible(paneId))

  const allStaticTiles = computed(() => {
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

  /** What the grid actually renders: empty while the rail is carrying these. */
  const tiles = computed(() => (staticTilesVisible.value ? allStaticTiles.value : []))

  /**
   * How wide the grid is allowed to get: exactly the width of one full row of
   * static tiles. The static row is the widest the grid ever needs to be, so
   * capping here means a wide window can never start a row longer than that —
   * the recently-opened tiles wrap underneath instead of stretching past it.
   *
   * Measured off the full static list even when it is not rendered, so hiding the
   * row keeps the remaining tiles on the same grid rather than letting them spread
   * across the whole window.
   */
  const gridMaxWidth = computed(
    () => allStaticTiles.value.length * (TILE_WIDTH + TILE_GAP) - TILE_GAP,
  )

  /**
   * The width of one full row of tiles at the current pane width — what the tile
   * block actually occupies on screen, which is a whole number of columns and so
   * rarely the full container width.
   *
   * The search bar is capped to this so it never runs wider than the tiles under
   * it. A plain percentage or a fixed max-width cannot do that: the grid is a
   * centered wrap container whose BOX is the full width even when the tiles
   * inside it wrap to four columns, so the bar has to be told the same column
   * arithmetic the grid wraps by.
   */
  const tileRowWidth = computed(() => {
    const available = Math.min(containerWidth.value || 320, gridMaxWidth.value)
    const columns = Math.max(1, Math.floor((available + TILE_GAP) / (TILE_WIDTH + TILE_GAP)))
    return columns * (TILE_WIDTH + TILE_GAP) - TILE_GAP
  })


  /**
   * The folder tiles, shown between the static tiles and the documents.
   *
   * Folders get half the budget, or MAX_FOLDER_TILES — whichever is lower. They earn
   * their points from the same opens the documents do, so an unsplit budget would let
   * a folder opened all morning push the documents off the page entirely — and the two
   * are not substitutes for each other. MAX_FOLDER_TILES sits BELOW half the budget on
   * purpose, so it is the binding limit rather than dead code: folders are the smaller
   * of the two lists in practice, and the documents get the slack.
   *
   * The half is rounded down, so an odd budget gives the spare slot to the documents.
   * Both limits are ceilings on the folders rather than reservations, so with the
   * documents switched off the folders still stop at MAX_FOLDER_TILES and the rest of
   * the budget simply goes unused rather than filling the page with folders.
   */
  const visibleFrequentFolderList = computed(() => {
    if (!showFrequentFolders.value) return []
    const budget = MAX_DYNAMIC_TILES
    const half = showRecentlyOpened.value ? Math.floor(budget / 2) : budget
    return frequentFolderList.value.slice(0, Math.min(MAX_FOLDER_TILES, half))
  })

  /**
   * The document tiles. They take the rest of the budget, including whatever the
   * folders did not claim — a half-share is a ceiling on the folders, not a
   * reservation held empty on their behalf.
   */
  const visibleRecentlyOpenedList = computed(() => {
    if (!showRecentlyOpened.value) return []
    if (!recentlyOpenedList.value.length) return []
    const room = MAX_DYNAMIC_TILES - visibleFrequentFolderList.value.length
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
    tileRowWidth,
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
