/**
 * Keys the full-text search results list takes beyond the shared combobox model
 * (useInputListNavigation). Pure: the list's keydown handler asks, this answers.
 */

/**
 * Space toggles the highlighted result's preview ("הצג עוד") — the keyboard
 * counterpart of the header's toggle button.
 *
 * Only with a highlight: without one Space is the scroller's own page-down. Never
 * from a text field: the search field forwards every key to the list, and a space
 * typed there is part of the query. Modified Space is left to whoever owns it.
 */
export function wantsPreviewToggle(event: KeyboardEvent, hasHighlight: boolean): boolean {
  if (event.code !== 'Space' || !hasHighlight) return false
  if (event.ctrlKey || event.metaKey || event.altKey) return false
  const target = event.target
  if (target instanceof HTMLElement) {
    if (target.isContentEditable || ['INPUT', 'TEXTAREA'].includes(target.tagName)) return false
  }
  return true
}
