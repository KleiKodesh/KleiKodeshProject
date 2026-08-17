import {
  idbTabsGet,
  idbTabsSet,
  idbTabsDelete,
  idbTabsDeleteByPrefix,
} from '@/utils/persistence'
import { useWorkspaceStore } from './workspaceStore'

/**
 * Key layout for the `app-tabs` IndexedDB database — the record shape of this slice,
 * not a settings name. One record per workspace×tab, one per workspace×tab×book, and
 * a prefix that matches every book record beneath a tab so teardown can scan for it.
 */
const KEYS = {
  tab: (wsId: string, tabId: string) => `tab:${wsId}:${tabId}`,
  book: (wsId: string, tabId: string, bookId: number) => `book:${wsId}:${tabId}:${bookId}`,
  tabPrefix: (wsId: string, tabId: string) => `book:${wsId}:${tabId}:`,
} as const

export interface TabState {
  searchCheckedBookIds?: number[] // absent/null means "all checked" (default)
  searchAtFilters?: string[]      // @ tokens from the search input, e.g. ["בראשית", "בבלי"]
  searchZoom?: number             // per-tab zoom level for the search results page (50–200)
  searchScrollIndex?: number      // virtual scroller item index for scroll restore
  searchScrollOffset?: number     // virtual scroller item offset for scroll restore
  searchSortOrder?: import('@/features/full-text-search/fullTextSearchTypes').FullTextSearchSortOrder // per-tab FTS result sort ('lineId' | 'relevance')
  htmlViewScrollTop?: number      // scroll position (px) for /html-view tabs (HTML and TXT files)
  txtViewZoom?: number            // per-tab zoom level for /txt-view tabs (50–400)
}

export interface BookState {
  scrollIndex: number
  scrollOffset: number
  selectedLineId?: number | null
  zoom?: number
  autoSelectTopLine?: boolean
  /** Both commentary panels' saved place, keyed by slot. */
  commentaryPanels?: import('@/features/book-view/bookViewTypes').CommentaryPanelPersistStates
  /** The TOC side panel, which survives tab switches the same way the panels do. */
  toc?: import('@/features/book-view/bookViewTypes').TocPersistState
}

/**
 * The `app-tabs` slice of persistence — everything keyed by workspace + tab.
 *
 * Two kinds of state live here, both scoped the same way:
 *   - `TabState`  per tab: search filters, scroll restore, per-tab zoom
 *   - `BookState` per tab *and* book: reading position, commentary layout
 *
 * Per-book state that is not tab-scoped (the global last-read position) lives in
 * `bookLastRead.ts` — separate database, separate lifetime, survives tab close.
 *
 * A plain module rather than a Pinia store: it holds no reactive state, and both
 * `tabStore` (for teardown) and feature composables (for read/write) use it, so it
 * cannot live in a feature folder. `tabStore` re-exports the read/write functions,
 * so callers reach it through the store as before.
 */

/** Resolved per call, never cached — switching workspaces must change which keys we address. */
function workspaceId(): string {
  return useWorkspaceStore().activeId
}

// ── In-memory cache ───────────────────────────────────────────────────────────
// One entry per open tab×book. Bounded by what the user has open, and evicted by
// deleteAllStateForTab when a tab closes, so it needs no size cap. A closed tab's
// reading position is not lost with it — it was copied into the location record in
// `recentLocations` as the tab navigated away or closed.

const bookStateCache = new Map<string, BookState | null>()

/**
 * Cache-key builders. Kept beside each other deliberately: the entry key and the
 * eviction prefix must agree on their separator, and a mismatch would silently
 * stop eviction from matching anything.
 */
function bookCacheKey(wsId: string, tabId: string, bookId: number): string {
  return `${wsId}:${tabId}:${bookId}`
}
function bookCacheKeyPrefixForTab(wsId: string, tabId: string): string {
  return `${wsId}:${tabId}:`
}

/**
 * Pending save promise. A tab being opened awaits this before reading, so the
 * outgoing tab's async IDB write is guaranteed to have committed first.
 */
let pendingBookStateSave: Promise<void> | null = null

// ── TabState (per tab) ────────────────────────────────────────────────────────

export function getTabViewState(tabId: string): Promise<TabState | null> {
  return idbTabsGet<TabState>(KEYS.tab(workspaceId(), tabId))
}

export function setTabViewState(tabId: string, state: TabState): Promise<void> {
  tabStateCache.set(KEYS.tab(workspaceId(), tabId), state)
  return idbTabsSet(KEYS.tab(workspaceId(), tabId), state)
}

/**
 * The last `TabState` written this session, or null if none was. Synchronous by
 * design: callers that need it (capturing a location's position as a tab navigates
 * away) run inside the navigation itself and cannot await, and the value they want
 * was written moments earlier by the view they are leaving.
 */
export function peekTabViewState(tabId: string): TabState | null {
  return tabStateCache.get(KEYS.tab(workspaceId(), tabId)) ?? null
}

// Mirrors bookStateCache below: written through on save so a synchronous peek can
// see it. Bounded by the number of tabs, and dropped with the tab.
const tabStateCache = new Map<string, TabState>()

// ── BookState (per tab + book) ────────────────────────────────────────────────

export function getBookViewState(tabId: string, bookId: number): Promise<BookState | null> {
  const wsId = workspaceId()
  const cacheKey = bookCacheKey(wsId, tabId, bookId)
  if (bookStateCache.has(cacheKey)) return Promise.resolve(bookStateCache.get(cacheKey)!)
  const read = async () => {
    const value = await idbTabsGet<BookState>(KEYS.book(wsId, tabId, bookId))
    bookStateCache.set(cacheKey, value)
    return value
  }
  return pendingBookStateSave ? pendingBookStateSave.then(read) : read()
}

export function setBookViewState(tabId: string, bookId: number, state: BookState): Promise<void> {
  const wsId = workspaceId()
  bookStateCache.set(bookCacheKey(wsId, tabId, bookId), state)
  pendingBookStateSave = idbTabsSet(KEYS.book(wsId, tabId, bookId), state)
  return pendingBookStateSave
}

/** Synchronous counterpart to getBookViewState — see peekTabViewState for why. */
export function peekBookViewState(tabId: string, bookId: number): BookState | null {
  return bookStateCache.get(bookCacheKey(workspaceId(), tabId, bookId)) ?? null
}

export function clearBookViewState(tabId: string, bookId: number): Promise<void> {
  const wsId = workspaceId()
  bookStateCache.delete(bookCacheKey(wsId, tabId, bookId))
  return idbTabsDelete(KEYS.book(wsId, tabId, bookId))
}

// ── Teardown ──────────────────────────────────────────────────────────────────

/**
 * Forget everything persisted for a closing tab: its `TabState`, every `BookState`
 * beneath it, and the matching in-memory cache entries.
 *
 * One call rather than three lines, because all three must happen together —
 * `closeTab`, `closePane2Tab`, and `closeAllTabs` previously each open-coded the
 * same sequence, and `closeAllTabs`'s copy recovered the tab id by splitting the
 * cache key on ':' (which assumes a workspace id containing no colon). Matching on
 * the prefix builder above removes that assumption.
 */
export function deleteAllStateForTab(tabId: string): void {
  const wsId = workspaceId()
  idbTabsDelete(KEYS.tab(wsId, tabId))
  idbTabsDeleteByPrefix(KEYS.tabPrefix(wsId, tabId))
  tabStateCache.delete(KEYS.tab(wsId, tabId))
  const prefix = bookCacheKeyPrefixForTab(wsId, tabId)
  for (const key of bookStateCache.keys()) {
    if (key.startsWith(prefix)) bookStateCache.delete(key)
  }
}
