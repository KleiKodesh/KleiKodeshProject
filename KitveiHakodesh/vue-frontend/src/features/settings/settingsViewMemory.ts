/**
 * What the settings page looked like when you left it, for THIS SESSION ONLY:
 * the search query and the scroll position.
 *
 * Both are needed to restore the view, and they must be restored TOGETHER — the
 * query decides which sections are visible, so a scroll offset means something
 * different under a different query. Restoring the offset alone would land in the
 * wrong place whenever a filter was active.
 *
 * The set of hidden sections is NOT stored: `applyFilter` recomputes it from the
 * query every time, so the query is the whole state and the hidden set is a
 * derivation of it. Storing both would be storing the same fact twice.
 *
 * Plain module variables, not a per-tab map: settings is unique per pane
 * (SINGLE_TAB_ROUTES in tabStore), so at most one settings tab exists and there is
 * nothing to key by. Closing that tab clears both, so a freshly opened settings
 * page starts clean.
 *
 * Not persisted: the settings VALUES already live in the settings store, so this is
 * only the transient view state, and it should not outlive a reload.
 */
let scrollTop = 0
let searchQuery = ''

export function getSettingsScrollTop(): number {
  return scrollTop
}

export function setSettingsScrollTop(value: number): void {
  scrollTop = value
}

export function getSettingsSearchQuery(): string {
  return searchQuery
}

export function setSettingsSearchQuery(value: string): void {
  searchQuery = value
}

/** Called when a settings tab closes, so the next one opens clean. */
export function clearSettingsViewState(): void {
  scrollTop = 0
  searchQuery = ''
}
