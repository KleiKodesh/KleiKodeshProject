import { type MaybeRefOrGetter, toValue } from 'vue'
import { onClickOutside, useEventListener } from '@vueuse/core'

type MaybeElement = HTMLElement | null | undefined

/**
 * Closes a dropdown when the user clicks outside it OR when the browser window
 * loses focus (e.g. clicking into a WebView iframe or switching apps).
 *
 * Drop-in replacement for `onClickOutside` — same signature, same behaviour,
 * plus the window-blur case.
 *
 * @param target  - The dropdown root element ref (same as onClickOutside target)
 * @param handler - Called when the dropdown should close
 * @param options - Optional: `ignore` list forwarded to onClickOutside
 */
export function useDropdownClose(
  target: MaybeRefOrGetter<MaybeElement>,
  handler: (event?: FocusEvent | MouseEvent | PointerEvent | TouchEvent | Event) => void,
  options?: { ignore?: MaybeRefOrGetter<MaybeElement>[] },
) {
  onClickOutside(target, handler, { ignore: options?.ignore })
  useEventListener(window, 'blur', (e: FocusEvent) => {
    if (toValue(target)) handler(e)
  })
}
