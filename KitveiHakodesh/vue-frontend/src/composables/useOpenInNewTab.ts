import { hasNativeChromeTabs } from '@/webview-host/bridge'

/**
 * Should activating an item open it in a NEW tab instead of the current one?
 *
 * True when the user held Ctrl (Windows/Linux) or ⌘ (macOS) — matching the
 * browser convention for "open in new tab". For a mouse event, middle-click
 * (button 1) counts too. Works for both clicks (Ctrl+click) and keyboard
 * activation (Ctrl+Enter); a missing event never opens a new tab.
 *
 * Always false where there is no native tab strip (VSTO task pane, dev browser):
 * those hosts show one tab and render no list of the others, so a second tab could
 * only ever be a document the reader cannot see or get back to. Gating the gesture
 * here covers every call site at once — this is the one place that decides it.
 */
export function wantsNewTab(event?: MouseEvent | KeyboardEvent | null): boolean {
  if (!event) return false
  if (!hasNativeChromeTabs) return false
  if (event.ctrlKey || event.metaKey) return true
  return 'button' in event && event.button === 1
}

/**
 * Hover-tooltip hint (Hebrew) teaching how to open an item, one gesture per line
 * so the current-tab vs new-tab distinction is explicit. Appended below the
 * content tooltip of every clickable item that can open a document, so the
 * wording stays identical across the app. Kept here beside wantsNewTab — the
 * single source of truth for the new-tab gesture.
 *
 *   Click / Enter — open in the current tab
 *   Ctrl+click / Ctrl+Enter — open in a new tab
 *
 * Where new tabs are disabled the second line would teach a gesture that does
 * nothing, so only the first is shown.
 */
export const OPEN_IN_NEW_TAB_HINT = hasNativeChromeTabs
  ? 'לחיצה / Enter — פתיחה בכרטיסייה הנוכחית\n' +
    'Ctrl+לחיצה / Ctrl+Enter — פתיחה בכרטיסייה חדשה'
  : 'לחיצה / Enter — פתיחה בכרטיסייה הנוכחית'

/** Append the new-tab hint as a trailing line to an existing content tooltip. */
export function withNewTabHint(tooltip: string): string {
  return tooltip ? `${tooltip}\n${OPEN_IN_NEW_TAB_HINT}` : OPEN_IN_NEW_TAB_HINT
}
