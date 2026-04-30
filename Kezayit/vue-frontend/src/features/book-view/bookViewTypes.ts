/**
 * Shared types for the book-view feature.
 * Import from here — never from a component file.
 */

export type SearchMode = 'content' | 'commentary'
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
