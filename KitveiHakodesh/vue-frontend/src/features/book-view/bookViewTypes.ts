/**
 * Shared types for the book-view feature.
 * Import from here — never from a component file.
 */

/**
 * The book view hosts two independent commentary panels: one stacked below the
 * text, one beside it. They share the fetched commentary of the current line and
 * nothing else - filter tree, pin, scroll position and search are per slot, so
 * each panel can show a different commentator on the same verse.
 *
 * 'side' is only reachable on a wide pane (see isWideScreen in BookViewPage).
 */
export type CommentarySlot = 'bottom' | 'side'

export const COMMENTARY_SLOTS = ['bottom', 'side'] as const

/** Which view the search bar is searching: the book text, or one of the panels. */
export type SearchMode = 'content' | 'commentary-bottom' | 'commentary-side'

export function searchModeForSlot(slot: CommentarySlot): SearchMode {
  return slot === 'bottom' ? 'commentary-bottom' : 'commentary-side'
}

/** The panel a search mode targets, or null when it targets the book text. */
export function slotForSearchMode(mode: SearchMode): CommentarySlot | null {
  if (mode === 'commentary-bottom') return 'bottom'
  if (mode === 'commentary-side') return 'side'
  return null
}

export type SidePanelMode = 'toc' | 'commentary-tree'

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
  bookTitle: string       // display name — also persisted for convenience
  isChecked: boolean
  isInSearchResults: boolean
}

export function isCommentaryItemVisible(item: CommentaryVisibilityItem): boolean {
  return item.isChecked && item.isInSearchResults
}

/** Persisted state for the commentary tree panel. */
export interface CommentaryTreeState {
  searchQuery: string
  tokens: string[]
  visibilityList: CommentaryVisibilityItem[]
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
 * Everything one commentary panel persists per (tab, book). Both panels store one
 * of these under `BookState.commentaryPanels` / `LastReadState.commentaryPanels`,
 * keyed by slot, so neither panel can clobber the other's saved place.
 */
export interface CommentaryPanelPersistState {
  visible?: boolean
  scrollIndex?: number | null
  scrollOffset?: number | null
  filterState?: CommentaryTreeState
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
 * Each panel's currently-shown commentary group, captured at the instant of a user
 * action (line click, auto-select scroll) while `activePinnedGroup` is still valid.
 * A snapshot rather than a single value because the panels sit on different books.
 */
export type CommentaryPinSnapshot = Partial<Record<CommentarySlot, PinnedCommentaryGroup | null>>
