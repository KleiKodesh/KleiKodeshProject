import { computed } from 'vue'
import { useWindowSize } from '@vueuse/core'
import { isVstoEnvironment as isVsto } from '@/webview-host/bridge'

/**
 * Whether split view is offered at all: in the desktop app and the dev browser, never in
 * the Word task pane. Not limited by width - the reader decides what is usable.
 *
 * Every surface that offers a split reads this, so they cannot disagree about when it exists.
 */
export const isSplitViewAvailable = !isVsto

export function useSplitViewAvailable() {
  return computed(() => isSplitViewAvailable)
}

/** The width below which a second column inside one book view stops being usable. */
const SIDE_BY_SIDE_MIN_WIDTH = 768

/**
 * Whether one book view has the room to show a second column beside its text (the
 * HebrewBooks edition shown beside a book). A layout question, unrelated to split view.
 */
export function useSideBySideRoom() {
  const { width } = useWindowSize()
  return computed(() => !isVsto && width.value >= SIDE_BY_SIDE_MIN_WIDTH)
}
