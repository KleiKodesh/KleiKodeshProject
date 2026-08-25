import type { Tab, TabRoute } from './tabStore'

/**
 * A LOCATION — somewhere the reader has been, described well enough to return to
 * without help from any live tab.
 *
 * This is the shared record behind two of the app's three tab collections:
 *
 *   tabs        live windows onto a location   (tabStore.tabs, per window)
 *   recents     locations visited, persisted   (recentLocations, LRU-evicted)
 *   history     locations visited in ONE tab   (navHistory, in memory, dies with the tab)
 *
 * The browser parallel is deliberate: `tabs` are tabs, `recents` is History plus
 * Recently-closed, and `history` is a tab's Back/Forward stack. Same record, three
 * different scopes and eviction rules.
 *
 * A location is SELF-DESCRIBING — it carries its own scroll position rather than
 * pointing at a per-tab state record. That is what decouples it from tabs: a
 * recents row survives its tab, two rows can hold different places in one book,
 * and per-tab state can go back to dying with the tab.
 */

/**
 * Where in a document the reader was. Which fields are meaningful depends on the
 * route, so this is a union of every route's notion of "position" rather than one
 * shape pretending to fit all of them.
 */
export interface LocationPosition {
  // Book view (virtualized list: item index + pixel offset within it).
  scrollIndex?: number
  scrollOffset?: number
  selectedLineId?: number | null
  /** Both commentary panels' saved place, keyed by slot. */
  commentaryPanels?: import('@/features/book-view/bookViewTypes').CommentaryPanelPersistStates
  // HTML / TXT view (plain pixel scroll).
  htmlViewScrollTop?: number
  // PDF view.
  pdfPage?: number
  // Full-text search results (virtualized).
  searchScrollIndex?: number
  searchScrollOffset?: number
}

/** The identity half of a location — what document, and how to open it. */
export interface NavLocation {
  /** Stable id for this record. NOT a tab id: locations outlive tabs. */
  id: string
  route: TabRoute
  title: string
  /** Breadcrumb within the document, for the caption ("בראשית · פרק יג"). */
  tocPath?: string

  // What to open. Mirrors the Tab fields that identify a document.
  bookId?: number
  localFileName?: string
  localFilePath?: string
  localFileHbBookId?: string
  localFileHbBookTitle?: string
  isOtzariaAddin?: boolean
  searchQuery?: string

  /** Where in it the reader was. Absent when never captured. */
  position?: LocationPosition

  /** Monotonic recency stamp — highest is most recent. Drives LRU eviction. */
  recentStamp: number
}

/**
 * Identity of the DOCUMENT a location points at, ignoring position. Two locations
 * with the same key are the same document; recents dedupes on this so revisiting a
 * book bumps its row instead of stacking near-duplicates.
 */
export function locationKey(loc: NavLocation | Tab): string {
  if (loc.route === '/book-view' && loc.bookId !== undefined) return `book:${loc.bookId}`
  if (loc.localFileHbBookId) return `hb:${loc.localFileHbBookId}`
  if (loc.localFilePath) return `file:${loc.localFilePath}`
  if (loc.localFileName) return `filename:${loc.localFileName}`
  if (loc.route === '/search' && loc.searchQuery) return `search:${loc.searchQuery}`
  return `route:${loc.route}`
}

/**
 * Whether a location belongs in RECENTS — the persisted "documents I opened" list.
 *
 * Home, the singleton destinations and an empty search page are excluded: recents
 * is a curated list of documents worth returning to, and a row for הגדרות or a
 * blank search form is noise that would push real documents out under the LRU cap.
 */
export function isRecentsWorthy(tab: Pick<Tab, 'route' | 'searchQuery'>): boolean {
  if (tab.route === '/') return false
  if (tab.route === '/search' && !tab.searchQuery?.trim()) return false
  return !NON_DOCUMENT_ROUTES.has(tab.route)
}

/**
 * Whether a location belongs in a tab's BACK/FORWARD HISTORY — which is very nearly
 * everything, and deliberately NOT the same rule as recents.
 *
 * A browser's Back works from any page: go to Settings and Back returns you to the
 * article. If the singletons were skipped here, navigating book → מילון would record
 * nothing, so Back from the dictionary would either do nothing or jump two steps to
 * whatever preceded the book. Home is included for the same reason — pressing Home
 * and then Back is a normal thing to want.
 *
 * The only exclusion is a search page with no query: that is a blank form the reader
 * passed through, not a place, and Back should skip over it to the last real page.
 */
export function isHistoryWorthy(tab: Pick<Tab, 'route' | 'searchQuery'>): boolean {
  return !(tab.route === '/search' && !tab.searchQuery?.trim())
}

/** Routes that show a tool rather than a document. */
const NON_DOCUMENT_ROUTES = new Set<TabRoute>([
  '/settings',
  '/books',
  '/hebrewbooks',
  '/hebrew-calendar',
  '/dictionary',
  '/midot',
  '/file-search',
])

/** Builds a location record from a tab's current state. */
export function locationFromTab(tab: Tab, recentStamp: number, position?: LocationPosition): NavLocation {
  const loc: NavLocation = {
    id: `${tab.id}:${recentStamp}`,
    route: tab.route,
    title: tab.title,
    recentStamp,
  }
  if (tab.tocPath) loc.tocPath = tab.tocPath
  if (tab.bookId !== undefined) loc.bookId = tab.bookId
  if (tab.localFileName) loc.localFileName = tab.localFileName
  if (tab.localFilePath) loc.localFilePath = tab.localFilePath
  if (tab.localFileHbBookId) loc.localFileHbBookId = tab.localFileHbBookId
  if (tab.localFileHbBookTitle) loc.localFileHbBookTitle = tab.localFileHbBookTitle
  if (tab.isOtzariaAddin) loc.isOtzariaAddin = true
  if (tab.searchQuery) loc.searchQuery = tab.searchQuery
  if (position) loc.position = position
  return loc
}

/** The patch that navigates a tab TO a location. Position is applied separately. */
export function tabPatchForLocation(loc: NavLocation): Partial<Omit<Tab, 'id'>> {
  return {
    route: loc.route,
    title: loc.title,
    tocPath: loc.tocPath,
    bookId: loc.bookId,
    localFileName: loc.localFileName,
    localFilePath: loc.localFilePath,
    localFileHbBookId: loc.localFileHbBookId,
    localFileHbBookTitle: loc.localFileHbBookTitle,
    isOtzariaAddin: loc.isOtzariaAddin,
    searchQuery: loc.searchQuery,
  }
}
