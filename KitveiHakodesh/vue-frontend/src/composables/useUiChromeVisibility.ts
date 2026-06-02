import { ref } from 'vue'

/**
 * Session-only UI chrome visibility state.
 * Resets to defaults on page reload.
 * Keyboard shortcut: Alt+F to toggle title bar visibility.
 *
 * The window listener is registered once at module load time so that calling
 * useUiChromeVisibility() from multiple components never duplicates the handler.
 */

const titleBarVisible = ref(true)

// Single window-level listener — registered once for the lifetime of the app.
window.addEventListener('keydown', (e: KeyboardEvent) => {
  if (e.altKey && e.code === 'KeyF') {
    e.preventDefault()
    titleBarVisible.value = !titleBarVisible.value
  }
})

export function useUiChromeVisibility() {
  return {
    titleBarVisible,
  }
}
