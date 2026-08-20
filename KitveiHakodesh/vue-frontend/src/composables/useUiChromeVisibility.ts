import { ref } from 'vue'
import { useBookViewStore } from '@/stores/bookViewStore'

/**
 * Session-only UI chrome visibility state, scoped per pane.
 * Each pane has its own independent titleBarVisible ref so Ctrl+H in one pane
 * does not affect the other pane's title bar.
 * Resets to defaults on page reload.
 * Keyboard shortcut: Ctrl+H — handled in AppTitleBar's pane-scoped keydown block.
 *
 * (Scrollbar behavior is NOT here: the hidden-scrollbars setting is a pure
 * passthrough from settingsStore to the WebView2 environment's ScrollBarStyle —
 * see `scrollbarsHidden` in settingsStore.)
 */

const pane1TitleBarVisible = ref(true)
const pane2TitleBarVisible = ref(true)

export function useUiChromeVisibility(paneId: 1 | 2 = 1) {
  return {
    titleBarVisible: paneId === 1 ? pane1TitleBarVisible : pane2TitleBarVisible,
  }
}

/**
 * Reading mode — F9, handled in the app-wide block of `useAppTitleBarShortcuts`.
 *
 * A "check all / uncheck all" over the hideable chrome: title bars (Ctrl+H) and
 * book-view toolbars (Ctrl+B). There is no stored reading-mode flag — whether
 * it is "on" is derived from the individual states, so the individual
 * shortcuts keep working and F9 never fights them: if anything is still
 * visible, F9 hides everything; only when everything is already hidden does it
 * bring everything back.
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

  const everythingHidden = panesOnScreen.every(
    (paneId) => !titleBarByPane[paneId].value && !bookViewStore.getToolbarVisible(paneId),
  )
  const hideAll = !everythingHidden

  for (const paneId of [1, 2] as const) {
    titleBarByPane[paneId].value = !hideAll
    if (bookViewStore.getToolbarVisible(paneId) === hideAll) bookViewStore.toggleToolbar(paneId)
  }
}
