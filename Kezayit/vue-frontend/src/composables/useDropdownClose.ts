import { ref, toValue } from 'vue'
import { onClickOutside, useEventListener, type MaybeElementRef } from '@vueuse/core'

type MaybeElement = HTMLElement | null | undefined

/**
 * Closes a dropdown when the user clicks outside it OR when the browser window
 * loses focus (e.g. clicking into a WebView iframe or switching apps).
 *
 * Also solves the toggle-button race: when the outside click lands on the
 * toggle button, `justClosed` is set to `true` for the duration of that event
 * loop tick. The toggle handler should guard with:
 *
 *   function toggle() {
 *     if (closer.justClosed.value) return
 *     isOpen.value = !isOpen.value
 *   }
 *
 * This prevents the sequence: pointerdown closes → click on button reopens.
 *
 * @param target       - The dropdown root element ref
 * @param handler      - Called when the dropdown should close
 * @param options.ignore      - Extra elements that don't count as "outside"
 * @param options.toggleButton - The button that opens/closes the dropdown;
 *                               clicks on it suppress the handler so the
 *                               button's own click can handle the close
 */
export function useDropdownClose(
  target: MaybeElementRef<MaybeElement>,
  handler: (event?: FocusEvent | MouseEvent | PointerEvent | TouchEvent | Event) => void,
  options?: {
    ignore?: MaybeElementRef<MaybeElement>[]
    toggleButton?: MaybeElementRef<MaybeElement>
  },
) {
  const justClosed = ref(false)

  function close(e?: Parameters<typeof handler>[0]) {
    handler(e)
    if (options?.toggleButton) {
      justClosed.value = true
      // Reset after the click event that follows this pointerdown has fired
      setTimeout(() => {
        justClosed.value = false
      }, 0)
    }
  }

  onClickOutside(
    target,
    (e) => {
      const btn = toValue(options?.toggleButton)
      if (btn && btn.contains(e.target as Node)) {
        // The click is on the toggle button — suppress the handler here;
        // the button's click event will handle closing via justClosed guard
        justClosed.value = true
        setTimeout(() => {
          justClosed.value = false
        }, 0)
        return
      }
      close(e)
    },
    { ignore: options?.ignore },
  )

  useEventListener(window, 'blur', (e: FocusEvent) => {
    if (toValue(target)) close(e)
  })

  return { justClosed }
}
