import { ref, computed, onMounted, watch } from 'vue'
import { storeToRefs } from 'pinia'
import { documentIcon, iconKeyForRoute } from '@/utils/documentIcons'
import { dbReady } from '@/webview-host/seforimDb'
import { useSettingsStore } from '@/stores/settingsStore'
import { useTabStore } from '@/stores/tabStore'
import { useBooksDataStore } from '@/stores/booksDataStore'
import { locationKey, type NavLocation } from '@/stores/navLocation'
import { useRecentlyOpenedStore, type RecentlyOpenedEntry } from '@/stores/recentlyOpenedStore'
import { useFrequentFoldersStore, type FrequentFolderEntry } from '@/stores/frequentFoldersStore'
import { bookResumePath } from './homeResumePath'
import type { HomeSuggestionCard } from './homeSuggestionCard'

/**
 * The most folder and document cards shown between them. A flat count, never
 * derived from the pane width — see the README's budget rule.
 */
const MAX_HISTORY_CARDS = 18
/** The most folder cards ever shown, however much budget there is to go round. */
const MAX_FOLDER_CARDS = 6

export interface HomeHistoryCardActions {
  openRecent: (entry: RecentlyOpenedEntry, openInNewTab: boolean) => void
  openFolder: (path: string, openInNewTab: boolean) => void
}

/**
 * The history cards: recently opened documents, then frequently visited folders.
 *
 * A book card's path is the full place it resumes to — category › book › TOC
 * breadcrumb. The breadcrumb comes from tabStore's recents location for the same
 * document (recentlyOpenedStore itself stores no position).
 */
export function useHomeHistoryCards(actions: HomeHistoryCardActions) {
  const recentlyOpenedStore = useRecentlyOpenedStore()
  const frequentFoldersStore = useFrequentFoldersStore()
  const booksDataStore = useBooksDataStore()
  const tabStore = useTabStore()
  const { showRecentlyOpened, showFrequentFolders } = storeToRefs(useSettingsStore())

  const recentlyOpenedList = ref<RecentlyOpenedEntry[]>([])
  const frequentFolderList = ref<FrequentFolderEntry[]>([])

  // Folders get half the budget or MAX_FOLDER_CARDS, whichever is lower — a
  // ceiling, not a reservation; the documents take whatever is left.
  const visibleFolders = computed(() => {
    if (!showFrequentFolders.value) return []
    const half = showRecentlyOpened.value ? Math.floor(MAX_HISTORY_CARDS / 2) : MAX_HISTORY_CARDS
    return frequentFolderList.value.slice(0, Math.min(MAX_FOLDER_CARDS, half))
  })

  const visibleRecents = computed(() => {
    if (!showRecentlyOpened.value) return []
    const room = MAX_HISTORY_CARDS - visibleFolders.value.length
    return recentlyOpenedList.value.slice(0, Math.max(0, room))
  })

  /** tocPath per document key, from the recents locations tabStore keeps. */
  const tocPathByDocumentKey = computed(() => {
    const map = new Map<string, string>()
    for (const location of tabStore.recentLocations) {
      if (location.tocPath) map.set(locationKey(location), location.tocPath)
    }
    return map
  })

  function recentPath(entry: RecentlyOpenedEntry): string {
    if (entry.route === '/book-view' && entry.bookId !== undefined) {
      // The entry has the identity fields locationKey reads; it is not a full location.
      const tocPath = tocPathByDocumentKey.value.get(locationKey(entry as unknown as NavLocation))
      return bookResumePath(booksDataStore.allBooksMap.get(entry.bookId), entry.title, tocPath)
    }
    return entry.localFilePath || entry.localFileName || ''
  }

  const recentCards = computed<HomeSuggestionCard[]>(() =>
    visibleRecents.value.map((entry) => {
      const icon = documentIcon(iconKeyForRoute(entry.route, entry.isOtzariaAddin))
      return {
        key: `recent:${entry.key}`,
        title: entry.title,
        path: recentPath(entry),
        icon: icon.icon24,
        color: icon.color,
        open: (openInNewTab) => actions.openRecent(entry, openInNewTab),
        pinning: {
          pinned: !!entry.pinned,
          togglePin: () => (recentlyOpenedList.value = recentlyOpenedStore.togglePin(entry.key)),
          remove: () => (recentlyOpenedList.value = recentlyOpenedStore.removeEntry(entry.key)),
        },
      }
    }),
  )

  const folderCards = computed<HomeSuggestionCard[]>(() => {
    const icon = documentIcon('folder')
    return visibleFolders.value.map((entry) => ({
      key: `folder:${entry.path}`,
      title: entry.name,
      path: entry.path,
      icon: icon.icon24,
      color: icon.color,
      open: (openInNewTab) => actions.openFolder(entry.path, openInNewTab),
      pinning: {
        pinned: !!entry.pinned,
        togglePin: () => (frequentFolderList.value = frequentFoldersStore.togglePin(entry.path)),
        remove: () => (frequentFolderList.value = frequentFoldersStore.removeEntry(entry.path)),
      },
    }))
  })

  // The category path needs the catalog; load it once a database is available.
  watch(dbReady, (ready) => { if (ready) void booksDataStore.ensureLoaded() }, { immediate: true })

  onMounted(() => {
    // Not awaited by the page's focus logic — a cold first IndexedDB read is slow.
    recentlyOpenedStore.getList().then((list) => (recentlyOpenedList.value = list))
    frequentFoldersStore.getList().then((list) => (frequentFolderList.value = list))
  })

  return computed(() => [...recentCards.value, ...folderCards.value])
}
