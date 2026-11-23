import { nextTick } from 'vue'
import { useTabStore, type Tab } from '@/stores/tabStore'
import { useLocalFileStore } from '@/stores/localFileStore'
import { useSearchCacheStore } from '@/stores/searchCacheStore'
import {
  readAllStateForTab,
  writeAllStateForTab,
  searchCachePinOwner,
  type TabStateRecords,
} from '@/stores/tabStatePersistence'
import { capturePositionsForTab } from './persistenceFlush'

/**
 * Moving a tab between desktop-app windows (a tab dragged onto another window's strip,
 * or torn off into a new window). Each window is its own page with its own storage id,
 * so the tab travels as data, orchestrated by the C# host (AppViewerTabMove.cs):
 *
 *   1. source page: packTab   - save where the reader is, then hand over the tab's
 *                               persisted entry and every per-tab record
 *   2. target page: unpackTab - a new tab id here, the records written under this
 *                               window's storage id, the tab opened and active
 *   3. source page: closeMovedTab - only after the target acked, so nothing is lost
 *
 * The rule: a moved tab arrives exactly like a tab restored after a restart. Whatever
 * survives a restart survives a move; what does not (Back/Forward, live downloads,
 * unsaved PDF edits) does not travel either.
 */

/** What travels between the pages. Plain JSON: it crosses the host as a string. */
export interface MovedTab {
  format: 1
  /** The tab as persistTabs writes it (no transient fields). */
  entry: Tab
  records: TabStateRecords
  /** The search-cache entry the tab's results page pins, so its scroll restore stays instant. */
  searchCacheKey: string | null
}

/** Step 1, in the source page. Throws when the tab cannot move (gone, or unsaved PDF edits). */
export async function packTab(tabId: string): Promise<MovedTab> {
  const tabStore = useTabStore()
  if (!tabStore.isOpenTab(tabId)) throw new Error('no such tab')
  // Unsaved PDF edits do not survive a restart and the close prompt would stop the
  // move halfway, so the tab stays where it is.
  if (tabStore.hasUnsavedEdits(tabId)) throw new Error('unsaved edits')
  // The view writes where the reader is NOW into the stores (as a tab close does),
  // and the watchers that persist that get their tick.
  capturePositionsForTab(tabId)
  tabStore.captureCurrentPosition(tabId)
  await nextTick()
  const entry = tabStore.persistedEntryOf(tabId)
  if (!entry) throw new Error('no such tab')
  const [records, searchCacheKey] = await Promise.all([
    readAllStateForTab(tabId),
    useSearchCacheStore().pinnedKey(searchCachePinOwner(tabId)).catch(() => null),
  ])
  // A JSON round trip: no reactive proxies or live handles may leave the page.
  return JSON.parse(JSON.stringify({ format: 1, entry, records, searchCacheKey })) as MovedTab
}

/**
 * Step 2, in the target page. Returns the new tab's id once its records are stored -
 * only then may the source close its copy. `position` is where the tab was dropped
 * among the window's tabs (-1 = at the end).
 */
export async function unpackTab(moved: MovedTab, position: number): Promise<string> {
  if (!moved || moved.format !== 1 || !moved.entry?.route) throw new Error('not a moved tab')
  const tabStore = useTabStore()
  let writes: Promise<void> = Promise.resolve()
  const tab = tabStore.adoptMovedTab(moved.entry, position, (newTabId) => {
    writes = writeAllStateForTab(newTabId, moved.records ?? { tabState: null, bookStates: [] })
  })
  const pin = moved.searchCacheKey
    ? useSearchCacheStore().pin(searchCachePinOwner(tab.id), moved.searchCacheKey).catch(() => undefined)
    : Promise.resolve()
  // A local file reopens through the same path a restart takes (a HebrewBooks book is
  // found in its cache or downloaded again). Nothing to do when restoreLocalFileTabsLazily
  // has yet to run (the page is still booting): it picks the tab up itself.
  if (localFileRestoreReady && (tab.route === '/pdf-view' || tab.route === '/html-view') && !tab.localFileVirtualUrl) {
    void useLocalFileStore().restoreTab(tab.id)
  }
  await Promise.all([writes, pin])
  return tab.id
}

/** Step 3, in the source page: the target has the tab, so this copy goes like a closed tab. */
export function closeMovedTab(tabId: string): void {
  useTabStore().closeTab(tabId)
}

// Set by main.ts once restoreLocalFileTabsLazily has run: before that, a moved file tab
// is restored by it, as at any start.
let localFileRestoreReady = false
export function markLocalFileRestoreReady(): void {
  localFileRestoreReady = true
}

// ── Host entry points ────────────────────────────────────────────────────────

/** A tab a new window starts with, injected before boot (AppViewer.StartWithMovedTab). */
interface ArrivingTab {
  requestId: string
  moved: MovedTab
}

declare global {
  interface Window {
    __khsPackTab?: (requestId: string, tabId: string) => 'pending'
    __khsUnpackTab?: (requestId: string, movedJson: string, position: number) => 'pending'
    __khsCloseMovedTab?: (tabId: string) => 'closed'
    __khsKeepOnlyActiveTab?: () => 'closed'
    __khsArrivingTab?: ArrivingTab
  }
}

function postResult(requestId: string, work: Promise<unknown>): 'pending' {
  const post = (message: Record<string, unknown>) =>
    window.chrome?.webview?.postMessage({ id: '0', action: 'tabMoveResult', requestId, ...message })
  void work.then(
    (result) => post({ ok: true, result }),
    (error: unknown) => post({ ok: false, error: String((error as Error)?.message ?? error) }),
  )
  return 'pending'
}

/** Installs the functions the C# host calls. Once, at startup, when a host is present. */
export function installTabMoveHost(): void {
  window.__khsPackTab = (requestId, tabId) => postResult(requestId, packTab(String(tabId)))
  window.__khsUnpackTab = (requestId, movedJson, position) =>
    postResult(requestId, Promise.resolve().then(() => unpackTab(JSON.parse(movedJson) as MovedTab, Number(position))))
  window.__khsCloseMovedTab = (tabId) => {
    closeMovedTab(String(tabId))
    return 'closed'
  }
  // A window started ahead of a drop, now holding the dropped tab: its starter tab goes.
  window.__khsKeepOnlyActiveTab = () => {
    const tabStore = useTabStore()
    for (const id of tabStore.tabs.map((t) => t.id)) if (id !== tabStore.activeTabId) tabStore.closeTab(id)
    return 'closed'
  }
}

/**
 * A new window opened for a torn-off tab starts with that tab instead of a home tab.
 * Called by main.ts after the stores are initialised and before mount, so the first
 * view drawn is the arrived tab. The window is new, so whatever else its tab list
 * holds (a home tab) goes. The host learns the result like any unpack.
 */
export function unpackArrivingTab(): void {
  const arriving = window.__khsArrivingTab
  if (!arriving?.requestId) return
  delete window.__khsArrivingTab
  const tabStore = useTabStore()
  const others = tabStore.tabs.map((t) => t.id)
  // unpackTab opens the tab synchronously (its first await comes after), so the others
  // are gone before anything renders.
  const work = unpackTab(arriving.moved, -1)
  if (tabStore.tabs.length > others.length) for (const id of others) tabStore.closeTab(id)
  postResult(arriving.requestId, work)
}
