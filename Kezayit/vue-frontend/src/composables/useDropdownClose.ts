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
    closeOnBlur?: boolean
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

  if (options?.closeOnBlur !== false) {
    useEventListener(window, 'blur', (e: FocusEvent) => {
      // Use setTimeout so document.activeElement settles before we check focus.
      setTimeout(() => {
        if (toValue(target) && !document.hasFocus()) close(e)
      }, 0)
    })
  }

  return { justClosed }
}
