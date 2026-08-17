/**
 * Where the settings page was scrolled to, for THIS SESSION ONLY.
 *
 * One number, not a per-tab map: settings is unique per pane (SINGLE_TAB_ROUTES in
 * tabStore), so at most one settings tab exists at a time and there is nothing to
 * key by. Closing that tab clears it, so a freshly opened settings page always
 * starts at the top.
 *
 * Not persisted: the settings VALUES already live in the settings store, so scroll
 * position is the only thing a tab switch loses — and it should not outlive a reload.
 */
let scrollTop = 0

export function getSettingsScrollTop(): number {
  return scrollTop
}

export function setSettingsScrollTop(value: number): void {
  scrollTop = value
}

/** Called when a settings tab closes, so the next one opens at the top. */
export function clearSettingsScrollTop(): void {
  scrollTop = 0
}
