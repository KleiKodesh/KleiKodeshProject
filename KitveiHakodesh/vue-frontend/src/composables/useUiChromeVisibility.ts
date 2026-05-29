import { ref } from 'vue'
import { useEventListener } from '@vueuse/core'

/**
 * Session-only UI chrome visibility state.
 * Resets to defaults on page reload.
 * Keyboard shortcut: F11 to toggle title bar visibility.
 */

const titleBarVisible = ref(true)

export function useUiChromeVisibility() {
  useEventListener('keydown', (e: KeyboardEvent) => {
    if (e.key === 'F11') {
      e.preventDefault()
      titleBarVisible.value = !titleBarVisible.value
    }
  })

  return {
    titleBarVisible,
  }
}
