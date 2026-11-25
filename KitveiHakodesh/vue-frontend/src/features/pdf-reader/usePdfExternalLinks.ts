import type { Ref } from 'vue'
import { useEventListener } from '@vueuse/core'
import { openUrlInSystemBrowser } from '@/webview-host/bridge'

/** Web and mail links open in the system browser; anything else (file:, javascript:) is not followed. */
const BROWSER_LINK = /^(https?|mailto|ftp):/i

/**
 * Links inside the pages (link annotations and PDF.js's auto-linked URLs) must never navigate
 * the app page or open an app window: external ones go to the system browser, as the outline's.
 * Internal links (href "#...") keep PDF.js's own handling (linkService jumps).
 */
export function usePdfExternalLinks(container: Ref<HTMLElement | null>) {
  function onClick(e: MouseEvent) {
    if (e.button > 1) return
    const link = (e.target as Element | null)?.closest?.('a[href]') as HTMLAnchorElement | null
    if (!link || !container.value?.contains(link)) return
    const href = link.getAttribute('href') ?? ''
    if (href === '' || href.startsWith('#')) return
    e.preventDefault()
    e.stopPropagation()
    if (BROWSER_LINK.test(link.href)) void openUrlInSystemBrowser(link.href)
  }
  // Capture: before PDF.js's own link handlers and the browser's default (left and middle click).
  useEventListener(container, 'click', onClick, { capture: true })
  useEventListener(container, 'auxclick', onClick, { capture: true })
}
