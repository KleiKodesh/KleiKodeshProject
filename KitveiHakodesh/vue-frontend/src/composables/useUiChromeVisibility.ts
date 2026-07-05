import { ref } from 'vue'

/**
 * Session-only UI chrome visibility state.
 * Resets to defaults on page reload.
 * Keyboard shortcut: Ctrl+H — handled in AppTitleBar's pane-scoped keydown block.
 */

const titleBarVisible = ref(true)

export function useUiChromeVisibility() {
  return {
    titleBarVisible,
  }
}
