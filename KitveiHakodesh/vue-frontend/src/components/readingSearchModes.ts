/**
 * The reading pages' search targets - shared by the in-text search bar (ReadingSearchBar) and the
 * book view: the text itself, or one of the book view's commentary panels. A page without
 * commentary panels (FastDocView) only ever uses 'content'.
 */
export type CommentarySlot = 'bottom' | 'side' | 'side-left'

/** Which view the search bar is searching: the text, or one of the panels. */
export type SearchMode = 'content' | `commentary-${CommentarySlot}`

export function searchModeForSlot(slot: CommentarySlot): SearchMode {
  return `commentary-${slot}`
}

/** The panel a search mode targets, or null when it targets the text. */
export function slotForSearchMode(mode: SearchMode): CommentarySlot | null {
  if (mode === 'content') return null
  return mode.slice('commentary-'.length) as CommentarySlot
}
