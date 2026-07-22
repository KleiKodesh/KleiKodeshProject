/**
 * Should activating an item open it in a NEW tab instead of the current one?
 *
 * True when the user held Ctrl (Windows/Linux) or ⌘ (macOS) — matching the
 * browser convention for "open in new tab". For a mouse event, middle-click
 * (button 1) counts too. Works for both clicks (Ctrl+click) and keyboard
 * activation (Ctrl+Enter); a missing event never opens a new tab.
 */
export function wantsNewTab(event?: MouseEvent | KeyboardEvent | null): boolean {
  if (!event) return false
  if (event.ctrlKey || event.metaKey) return true
  return 'button' in event && event.button === 1
}

/**
 * Hover-tooltip hint (Hebrew) teaching the open-in-new-tab shortcut. Appended as
 * its own line to the content tooltip of every clickable item that can open a
 * document, so the wording stays identical across the app. Kept here beside
 * wantsNewTab — the single source of truth for the new-tab gesture.
 *
 * "Click to open • Ctrl+click / Ctrl+Enter — new tab"
 */
export const OPEN_IN_NEW_TAB_HINT =
  'לחיצה לפתיחה • Ctrl+לחיצה / Ctrl+Enter — כרטיסייה חדשה'

/** Append the new-tab hint as a trailing line to an existing content tooltip. */
export function withNewTabHint(tooltip: string): string {
  return tooltip ? `${tooltip}\n${OPEN_IN_NEW_TAB_HINT}` : OPEN_IN_NEW_TAB_HINT
}
