import { ref } from 'vue'

/**
 * Session-only UI chrome visibility state, scoped per pane.
 * Each pane has its own independent titleBarVisible ref so Ctrl+H in one pane
 * does not affect the other pane's title bar.
 * Resets to defaults on page reload.
 * Keyboard shortcut: Ctrl+H — handled in AppTitleBar's pane-scoped keydown block.
 */

const pane1TitleBarVisible = ref(true)
const pane2TitleBarVisible = ref(true)

export function useUiChromeVisibility(paneId: 1 | 2 = 1) {
  return {
    titleBarVisible: paneId === 1 ? pane1TitleBarVisible : pane2TitleBarVisible,
  }
}
