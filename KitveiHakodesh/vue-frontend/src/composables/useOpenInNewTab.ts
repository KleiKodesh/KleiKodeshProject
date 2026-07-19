/**
 * Should activating an item open it in a NEW tab instead of the current one?
 *
 * True when the user held Ctrl (Windows/Linux) or ⌘ (macOS) while clicking —
 * matching the browser convention for "open in new tab". Middle-click (button 1)
 * counts too. Keyboard activation (Enter) passes no event and therefore never
 * opens a new tab, which is the intended default.
 */
export function wantsNewTab(event?: MouseEvent | null): boolean {
  if (!event) return false
  return event.ctrlKey || event.metaKey || event.button === 1
}
