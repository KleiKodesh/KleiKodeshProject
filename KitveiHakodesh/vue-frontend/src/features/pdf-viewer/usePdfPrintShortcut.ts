import { inject } from 'vue'
import { useEventListener } from '@vueuse/core'
import { useBookViewStore } from '@/stores/bookViewStore'

/**
 * Ctrl+P → print the PDF while focus is on the parent document.
 *
 * PDF.js already handles Ctrl+P — but only when focus is inside its iframe.
 * After a click anywhere in the surrounding app (title bar, toolbar), the
 * keydown fires on this document instead, where the app-wide handler in
 * `useAppTitleBarShortcuts` swallows Ctrl+P to block the browser's print
 * dialog. That swallow is preventDefault-only, so the event still reaches this
 * listener — forward the print into the iframe, where PDF.js's `beforeprint`
 * hook renders the pages exactly as if Ctrl+P had been pressed inside it.
 */
export function usePdfPrintShortcut(getIframe: () => HTMLIFrameElement | null) {
  const bookViewStore = useBookViewStore()

  useEventListener('keydown', (event: KeyboardEvent) => {
    // Alt is excluded because Ctrl+Alt+P is PDF.js's presentation-mode shortcut.
    if (!event.ctrlKey || event.altKey || event.code !== 'KeyP') return
    event.preventDefault()
    getIframe()?.contentWindow?.print()
  })
}
