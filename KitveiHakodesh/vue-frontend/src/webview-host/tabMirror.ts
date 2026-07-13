import { computed, ref, watch } from 'vue'
import { useTabStore } from '@/stores/tabStore'
import { useBookViewStore } from '@/stores/bookViewStore'
import { useRecentlyOpenedStore } from '@/stores/recentlyOpenedStore'
import { useLocalFileStore } from '@/stores/localFileStore'
import { notifyTabsChanged, isVstoEnvironment } from './bridge'
import { onWebviewEvent, isHosted } from './seforimDb'
import {
  activateTabAnyPane,
  closeTabAnyPane,
  openNewTabPane1,
} from '@/composables/useCrossPaneTabActions'
import type { Tab } from '@/stores/tabStore'
import type { MirroredTab, MirroredRecentItem } from './bridge'

/**
 * Mirrors the Vue tab store to the native chrome tab strip (FluentChromeTabsForm
 * hosted by C#) and applies native strip gestures back onto the store.
 *
 * Vue is the source of truth:
 *  - Outbound: any change to tab membership, titles, or the focused pane's active
 *    tab sends a full snapshot via the 'tabsChanged' bridge action, including the
 *    recently-opened documents that are NOT currently open (for the native
 *    tab-list dropdown's "נסגרו לאחרונה" section).
 *  - Inbound: strip gestures arrive as push events and are applied to the store,
 *    which in turn produces the next snapshot:
 *      chromeTabActivated      { tabId } — user clicked a strip tab
 *      chromeTabCloseRequested { tabId } — user clicked a strip close button
 *      chromeTabNewRequested             — user clicked "+" (or Ctrl+T on the strip)
 *      chromeRecentActivated   { key }   — user picked a recently-opened document
 *
 * Call once from main.ts after the stores are initialized. No-op in dev browser
 * and in the VSTO task pane (no native tab strip there).
 */

const RECENT_MAX = 8

/** Same identity rules as recentlyOpenedStore.deriveKey, applied to an open tab. */
function tabRecentKey(tab: Tab): string | null {
  if (tab.route === '/book-view' && tab.bookId !== undefined) return `book:${tab.bookId}`
  if (tab.localFileHbBookId) return `hb:${tab.localFileHbBookId}`
  if (tab.localFilePath) return `file:${tab.localFilePath}`
  if (tab.localFileName) return `filename:${tab.localFileName}`
  return null
}

export function initTabMirror(): void {
  if (!isHosted || isVstoEnvironment) return
  if (typeof window.__webviewAction !== 'function') return

  const tabStore = useTabStore()
  const bookViewStore = useBookViewStore()
  const recentlyOpenedStore = useRecentlyOpenedStore()

  // Recently opened documents not currently open in any tab. The store has no
  // reactivity of its own, so this ref is refreshed after every snapshot send —
  // tab changes and recently-opened changes always happen together.
  const recentItems = ref<MirroredRecentItem[]>([])

  function refreshRecent() {
    void recentlyOpenedStore.getList().then((list) => {
      const openKeys = new Set(tabStore.tabs.map(tabRecentKey).filter(Boolean))
      const next = list
        .filter((e) => !openKeys.has(e.key))
        .slice(0, RECENT_MAX)
        .map((e): MirroredRecentItem => ({ key: e.key, title: e.title }))
      if (JSON.stringify(next) !== JSON.stringify(recentItems.value)) {
        recentItems.value = next
      }
    })
  }

  // Sorted by id so Vue's MRU move-to-front reordering doesn't produce spurious
  // snapshots — the strip keeps its own stable visual order and only needs
  // membership, titles, and the active tab.
  const snapshot = computed(() => ({
    tabs: [...tabStore.tabs]
      .sort((a, b) => Number(a.id) - Number(b.id))
      .map((t): MirroredTab => ({ id: t.id, title: t.title, pane: t.pane === 2 ? 2 : 1 })),
    activeTabId:
      bookViewStore.splitViewEnabled && bookViewStore.focusedPaneId === 2
        ? tabStore.pane2ActiveTabId
        : tabStore.activeTabId,
    recent: recentItems.value,
  }))

  let lastSent = ''
  watch(
    snapshot,
    (snap) => {
      const json = JSON.stringify(snap)
      if (json !== lastSent) {
        lastSent = json
        notifyTabsChanged(snap.tabs, snap.activeTabId, snap.recent)
      }
      refreshRecent()
    },
    { immediate: true },
  )

  async function openRecent(key: string) {
    const entry = (await recentlyOpenedStore.getList()).find((e) => e.key === key)
    if (!entry) return
    bookViewStore.setFocusedPane(1)
    if (entry.route === '/book-view' && entry.bookId !== undefined) {
      tabStore.openTab({ route: '/book-view', title: entry.title, bookId: entry.bookId })
      return
    }
    // File entries: open a fresh tab, then let the shared history-restore flow
    // (same as the home page tiles) fill it in place.
    tabStore.openTab({ route: '/', title: entry.title })
    await useLocalFileStore().openFromHistory(entry)
  }

  onWebviewEvent((msg) => {
    switch (msg.event) {
      case 'chromeTabActivated':
        activateTabAnyPane(String(msg.tabId))
        break
      case 'chromeTabCloseRequested':
        closeTabAnyPane(String(msg.tabId))
        break
      case 'chromeTabNewRequested':
        openNewTabPane1()
        break
      case 'chromeRecentActivated':
        void openRecent(String(msg.key))
        break
    }
  })
}
