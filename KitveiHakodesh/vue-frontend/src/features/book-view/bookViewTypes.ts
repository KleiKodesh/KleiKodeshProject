/**
 * Shared types for the book-view feature.
 * Import from here — never from a component file.
 */

/**
 * The book view hosts three independent commentary panels: one stacked below the
 * text and one on each side of it. They share the fetched commentary of the
 * current line and nothing else - filter tree, pin, scroll position and search
 * are per slot, so each panel can show a different commentator on the same verse.
 *
 * 'side' is the column on the RTL start edge (physically right), 'side-left' the
 * one on the end edge. Both are only reachable on a wide pane (see isWideScreen
 * in BookViewPage); 'bottom' works at any width.
 */
export type CommentarySlot = 'bottom' | 'side' | 'side-left'

export const COMMENTARY_SLOTS = ['bottom', 'side', 'side-left'] as const

/** The slots that need a pane wide enough to sit beside the text. */
export const SIDE_COMMENTARY_SLOTS = ['side', 'side-left'] as const

export function isSideCommentarySlot(slot: CommentarySlot): boolean {
  return slot === 'side' || slot === 'side-left'
}

/** Which view the search bar is searching: the book text, or one of the panels. */
export type SearchMode = 'content' | `commentary-${CommentarySlot}`

export function searchModeForSlot(slot: CommentarySlot): SearchMode {
  return `commentary-${slot}`
}

/** The panel a search mode targets, or null when it targets the book text. */
export function slotForSearchMode(mode: SearchMode): CommentarySlot | null {
  if (mode === 'content') return null
  return mode.slice('commentary-'.length) as CommentarySlot
}

/**
 * Visibility state for one commentary entry in the tree panel.
 * bookId + sectionLabel + subSectionLabel uniquely identifies an entry
 * (the same book can appear under multiple sections).
 *
 * isVisible = isChecked && isInSearchResults
 * isInSearchResults defaults to true when no search is active.
 */
export interface CommentaryVisibilityItem {
  bookId: number
  sectionLabel: string    // e.g. "מפרשים"
  subSectionLabel: string // e.g. "ראשונים", or "" if none
  bookTitle: string       // display name — taken from the live group, never stored
  isChecked: boolean
  isInSearchResults: boolean
}

export function isCommentaryItemVisible(item: CommentaryVisibilityItem): boolean {
  return item.isChecked && item.isInSearchResults
}

/** Live state for the commentary tree panel. */
export interface CommentaryTreeState {
  searchQuery: string
  tokens: string[]
  visibilityList: CommentaryVisibilityItem[]
}

/**
 * The saved form of the above: the reader's search input, and nothing else.
 *
 * `visibilityList` is deliberately NOT persisted. Every field of it is derived —
 * `bookId`/`sectionLabel`/`bookTitle` are book data already in the seforim DB,
 * `isChecked` is a cache over the check-tree (persisted separately as `checkState`),
 * and `isInSearchResults` follows from `searchQuery`. `syncVisibilityList` rebuilds
 * the whole list from the live commentary groups as soon as they load, so a stored
 * copy is overwritten unread — it was ~115 duplicated book titles per panel in every
 * saved record, and restoring it flashed a screen of untitled rows.
 */
export interface CommentaryTreeStatePersist {
  searchQuery: string
  tokens: string[]
}

/**
 * Identifies a specific commentary group by book + section.
 * A book can appear in multiple sections (e.g. once as COMMENTARY and once as
 * REFERENCE), so bookId alone is not a unique key — sectionLabel and
 * subSectionLabel are required to pinpoint the exact group.
 */
export interface PinnedCommentaryGroup {
  bookId: number
  sectionLabel: string      // e.g. "מפרשים"
  subSectionLabel: string   // e.g. "ראשונים", or "" if none
}

/**
 * One panel's filter check-tree, flattened for storage. Mirrors the live state in
 * uncheckedCommentaryBooks.ts, which uses Maps and a Set — neither survives the
 * structured clone into IndexedDB.
 *
 * A book entry is [bookId, path, checked], where path is `${section}::${subSection}`.
 */
export interface CommentaryCheckStateSnapshot {
  /** Root ("show all") default; undefined = checked. */
  root?: boolean
  sections: [string, boolean][]
  subsections: [string, boolean][]
  books: [number, string, boolean][]
  expanded: string[]
}

/**
 * Everything one commentary panel persists per (tab, book). Both panels store one
 * of these under `BookState.commentaryPanels` / `LastReadState.commentaryPanels`,
 * keyed by slot, so neither panel can clobber the other's saved place.
 */
export interface CommentaryPanelPersistState {
  visible?: boolean
  scrollIndex?: number | null
  scrollOffset?: number | null
  filterState?: CommentaryTreeStatePersist
  /**
   * Which commentaries are ticked in the filter tree. Separate from `filterState`
   * because that only describes the current line's books, while this is the whole
   * virtual tree — see CommentaryCheckStateSnapshot.
   */
  checkState?: CommentaryCheckStateSnapshot
  pinnedGroup?: PinnedCommentaryGroup | null
  /** Divider position, 0.1-0.9: pane width for 'side', pane height for 'bottom'. */
  fraction?: number
  /** This panel's text zoom percentage (50-400). */
  zoom?: number
}

/** Both panels' persisted state. A missing slot means "never opened". */
export type CommentaryPanelPersistStates = Partial<
  Record<CommentarySlot, CommentaryPanelPersistState>
>

/**
 * What the panels hand over at save time: the same fields, but `filterState` is
 * still the panel's LIVE reactive tree state. The save path clones it into the
 * persisted form (dropping `isChecked`) before it reaches IndexedDB.
 */
export interface CommentaryPanelLiveState extends Omit<CommentaryPanelPersistState, 'filterState'> {
  filterState?: CommentaryTreeState
}

export type CommentaryPanelLiveStates = Partial<
  Record<CommentarySlot, CommentaryPanelLiveState>
>

/**
 * The TOC side panel's saved state, per (tab, book).
 *
 * The panel is `v-if`'d, so its whole subtree is destroyed when it closes and again
 * on every tab switch — none of this survives in component state. Expanded nodes are
 * stored as ids rather than a tree so the shape does not depend on the TOC's own.
 */
export interface TocPersistState {
  visible?: boolean
  searchQuery?: string
  /** Expanded node ids for the main TOC tree. */
  expanded?: number[]
  /** Expanded node ids for the alternate structure tree (daf, parasha, …). */
  altExpanded?: number[]
  scrollTop?: number
  altScrollTop?: number
  /** Which alternate structure the reader picked, when the book has several. */
  altStructureId?: number | null
}

/**
 * Each panel's currently-shown commentary group, captured at the instant of a user
 * action (line click, auto-select scroll) while `activePinnedGroup` is still valid.
 * A snapshot rather than a single value because the panels sit on different books.
 */
export type CommentaryPinSnapshot = Partial<Record<CommentarySlot, PinnedCommentaryGroup | null>>
