import { ref, computed, onMounted, type Component, type Ref } from 'vue'
import { storeToRefs } from 'pinia'
import {
  IconLibrary24Filled,
  IconFolder24Filled,
  IconBookOpen24Filled,
  IconApps24Filled,
  IconDatabase24Filled,
  IconArrowDownload24Filled,
  IconCalendarRtl24Filled,
  IconBookLetter24Filled,
  IconRuler24Filled,
  IconDocumentPdf24Filled,
  IconDocumentText24Filled,
  IconDocumentGlobe24Filled,
  IconPuzzlePiece24Regular,
} from '@iconify-prerendered/vue-fluent'
import { IconSettings24, IconSearchSparkle24 } from '@iconify-prerendered/vue-fluent-color'
import IconEverythingSearch from '@/components/IconEverythingSearch.vue'
import IconBookRtl24 from '@/components/IconBookRtl24.vue'
import { isHosted, dbReady } from '@/webview-host/seforimDb'
import { useSettingsStore } from '@/stores/settingsStore'
import {
  useRecentlyOpenedStore,
  type RecentlyOpenedEntry,
} from '@/stores/recentlyOpenedStore'

const TILE_WIDTH = 72
const TILE_GAP = 16

/** Hard ceiling on recently-opened tiles, regardless of available row space. */
const RECENTLY_OPENED_MAX = 20

const RECENTLY_OPENED_ICON_MAP: Record<string, { icon: Component; color: string }> = {
  '/book-view': { icon: IconBookRtl24, color: '#c1440e' },
  '/pdf-view': { icon: IconDocumentPdf24Filled, color: '#F40F02' },
  '/html-view': { icon: IconDocumentGlobe24Filled, color: '#0097fb' },
  '/txt-view': { icon: IconDocumentText24Filled, color: '#9e9e9e' },
}

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

  const tiles = computed(() => {
    const dbMissing = isHosted && !dbReady.value
    return [
      dbMissing
        ? { label: 'הורד מסד ספרים', icon: IconArrowDownload24Filled, color: '#B5451B' }
        : { label: 'ספרים', icon: IconLibrary24Filled, color: '#B5451B' },
      dbMissing
        ? { label: 'בחר מסד ספרים', icon: IconDatabase24Filled, color: '#3478f6' }
        : { label: 'חיפוש', icon: IconSearchSparkle24 },
      { label: 'היברו-בוקס', icon: IconBookOpen24Filled, color: '#D94F1E' },
      { label: 'פתח קובץ', icon: IconFolder24Filled, color: 'var(--status-warning)' },
      { label: 'חיפוש קבצים', icon: IconEverythingSearch, iconScale: 0.93 },
      { label: 'מילון', icon: IconBookLetter24Filled, color: '#7b5ea7' },
      { label: 'לוח שנה', icon: IconCalendarRtl24Filled, color: '#2e7d32' },
      { label: 'מידות ושיעורים', icon: IconRuler24Filled, color: '#8b6914' },
      { label: 'סביבות עבודה', icon: IconApps24Filled, color: '#6b7fc4' },
      { label: 'הגדרות', icon: IconSettings24 },
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
    if (entry.route === '/html-view' && entry.isOtzariaAddin)
      return { icon: IconPuzzlePiece24Regular, color: '#7b5ea7' }
    return RECENTLY_OPENED_ICON_MAP[entry.route]!
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
