import { computed } from 'vue'
import { useWindowSize } from '@vueuse/core'
import { isVstoEnvironment as isVsto } from '@/webview-host/bridge'

/** Split view needs the room for two usable panes. */
export const SPLIT_VIEW_MIN_WIDTH = 768

/**
 * Whether the split-view toggle may be offered at all — never in the VSTO task pane, and
 * not below the width where two panes stop being usable.
 *
 * Both surfaces that offer the toggle read this, so they cannot disagree about when it
 * exists: the title bar owns it normally, and the nav sidebar takes it over while the
 * sidebar is up. App.vue measures the app element instead of the window (it auto-disables
 * split view when that element shrinks) but shares the threshold above.
 */
export function useSplitViewAvailable() {
  const { width } = useWindowSize()
  return computed(() => !isVsto && width.value >= SPLIT_VIEW_MIN_WIDTH)
}
