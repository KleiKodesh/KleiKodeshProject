import { ref } from 'vue'
import { useBookViewStore } from '@/stores/bookViewStore'

/**
 * Session-only UI chrome visibility state, scoped per pane.
 * Each pane has its own independent titleBarVisible ref so Ctrl+H in one pane
 * does not affect the other pane's title bar.
 * Resets to defaults on page reload.
 * Keyboard shortcut: Ctrl+H — handled in AppTitleBar's pane-scoped keydown block.
 */

const pane1TitleBarVisible = ref(true)
const pane2TitleBarVisible = ref(true)

/**
 * App-wide (not per-pane): scrollbars are hidden by a class on the root element,
 * and there is only one root. Keyboard shortcut: Ctrl+Shift+H — handled in the
 * app-wide block of `useAppTitleBarShortcuts`. The CSS lives in `main.css` under
 * `:root.hide-scrollbars`. The class cannot reach into iframe documents
 * (HTML/txt viewer, PDF.js) — pages owning an iframe propagate this state into
 * it with `useIframeScrollbarsHidden`.
 */
const scrollbarsHidden = ref(false)

export function toggleScrollbarsHidden() {
  scrollbarsHidden.value = !scrollbarsHidden.value
  document.documentElement.classList.toggle('hide-scrollbars', scrollbarsHidden.value)
}

export function useUiChromeVisibility(paneId: 1 | 2 = 1) {
  return {
    titleBarVisible: paneId === 1 ? pane1TitleBarVisible : pane2TitleBarVisible,
    scrollbarsHidden,
  }
}

/**
 * Reading mode — F9, handled in the app-wide block of `useAppTitleBarShortcuts`.
 *
 * A "check all / uncheck all" over the hideable chrome: title bars (Ctrl+H),
 * book-view toolbars (Ctrl+B) and scrollbars (Ctrl+Shift+H). There is no stored
 * reading-mode flag — whether it is "on" is derived from the individual states,
 * so the individual shortcuts keep working and F9 never fights them:
 * if anything is still visible, F9 hides everything; only when everything is
 * already hidden does it bring everything back.
 *
 * Derivation only looks at panes that are on screen (pane 2 counts only in
 * split view), but applying sets both panes so a pane opened later matches.
 * Toolbar visibility goes through the store's own toggle, keeping its
 * persistence semantics identical to pressing Ctrl+B.
 */
export function toggleReadingMode() {
  const bookViewStore = useBookViewStore()
  const titleBarByPane = { 1: pane1TitleBarVisible, 2: pane2TitleBarVisible } as const
  const panesOnScreen: readonly (1 | 2)[] = bookViewStore.splitViewEnabled ? [1, 2] : [1]

  const everythingHidden =
    scrollbarsHidden.value &&
    panesOnScreen.every(
      (paneId) => !titleBarByPane[paneId].value && !bookViewStore.getToolbarVisible(paneId),
    )
  const hideAll = !everythingHidden

  if (scrollbarsHidden.value !== hideAll) toggleScrollbarsHidden()
  for (const paneId of [1, 2] as const) {
    titleBarByPane[paneId].value = !hideAll
    if (bookViewStore.getToolbarVisible(paneId) === hideAll) bookViewStore.toggleToolbar(paneId)
  }
}
