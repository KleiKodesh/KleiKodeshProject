import { computed } from 'vue'
import { useWindowSize } from '@vueuse/core'
import { isVstoEnvironment as isVsto } from '@/webview-host/bridge'

/** The width below which a second column inside one book view stops being usable. */
const SIDE_BY_SIDE_MIN_WIDTH = 768

/**
 * Whether one book view has the room to show a second column beside its text (the
 * HebrewBooks edition shown beside a book).
 */
export function useSideBySideRoom() {
  const { width } = useWindowSize()
  return computed(() => !isVsto && width.value >= SIDE_BY_SIDE_MIN_WIDTH)
}
