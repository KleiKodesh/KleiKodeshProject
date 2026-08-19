import { watch } from 'vue'
import { useUiChromeVisibility } from './useUiChromeVisibility'

const AUTO_HIDE_STYLE_ID = '__kitvei-auto-hide-scrollbars'
const SCROLLING_CLASS = '__kitvei-scrollbars-scrolling'
// !important so it beats the thin-scrollbar style the C#-injected
// IframeScrollScript re-adds on every theme message.
// The bare html selector matters: the frame's viewport scrollbar styles come
// from the root element itself, which `html *` alone would miss.
const AUTO_HIDE_STYLE_CSS =
  `html:not(.${SCROLLING_CLASS}), html:not(.${SCROLLING_CLASS}) * { scrollbar-color: transparent transparent !important; }` +
  `html:not(.${SCROLLING_CLASS})::-webkit-scrollbar-thumb, html:not(.${SCROLLING_CLASS}) *::-webkit-scrollbar-thumb { background: transparent !important; }`
const SCROLLING_LINGER_MS = 1000

/**
 * Keeps one iframe following the app-wide scrollbars mode — static or
 * Windows-11-style auto-hide (Ctrl+Shift+H, F9 reading mode, or the settings
 * page) — so framed documents behave like an integral part of the app: the
 * classes on the app root cannot reach into another document.
 *
 * Two delivery paths, chosen per call by whether the frame's document is
 * reachable (`contentDocument` is null for cross-origin frames):
 *   - Same-origin (the PDF.js viewer, and local files in browser dev mode):
 *     a style element injected straight into the frame's document, plus a
 *     capture scroll listener on the frame's window that flags scroll activity
 *     on the frame's own root element — mirroring what `useUiChromeVisibility`
 *     does for the app document.
 *   - Cross-origin (local files on kitvei-localfile-N in the WebView2 host):
 *     a { type: 'htmlViewScrollbars', autoHide } postMessage, applied inside
 *     the frame by the C#-injected IframeScrollScript (JsBridge.cs), which
 *     tracks its own scroll activity — the same channel theme sync and scroll
 *     restore use.
 *
 * The owning page must call `apply` from the iframe's load handler: a freshly
 * loaded document always starts static, whatever the app state, and navigation
 * discards the previous document's style and listener. Mode changes while the
 * frame is alive are pushed by the watcher.
 */
export function useIframeScrollbarsAutoHide(getIframe: () => HTMLIFrameElement | null) {
  const { scrollbarsAutoHide } = useUiChromeVisibility()

  let scrollingTimer: number | null = null

  function onFrameScroll() {
    const frameRoot = getIframe()?.contentDocument?.documentElement
    if (!frameRoot) return
    frameRoot.classList.add(SCROLLING_CLASS)
    if (scrollingTimer !== null) clearTimeout(scrollingTimer)
    scrollingTimer = window.setTimeout(() => {
      scrollingTimer = null
      getIframe()?.contentDocument?.documentElement.classList.remove(SCROLLING_CLASS)
    }, SCROLLING_LINGER_MS)
  }

  function apply() {
    const iframe = getIframe()
    if (!iframe) return
    const autoHide = scrollbarsAutoHide.value
    const doc = iframe.contentDocument
    if (doc) {
      const existing = doc.getElementById(AUTO_HIDE_STYLE_ID)
      if (autoHide && !existing) {
        const style = doc.createElement('style')
        style.id = AUTO_HIDE_STYLE_ID
        style.textContent = AUTO_HIDE_STYLE_CSS
        ;(doc.head ?? doc.documentElement)?.appendChild(style)
      } else if (!autoHide && existing) {
        existing.remove()
      }
      // The listener dies with the frame's inner window on navigation and
      // re-adding an identical one is a no-op, so both branches are idempotent.
      if (autoHide) {
        iframe.contentWindow?.addEventListener('scroll', onFrameScroll, {
          capture: true,
          passive: true,
        })
      } else {
        iframe.contentWindow?.removeEventListener('scroll', onFrameScroll, { capture: true })
        doc.documentElement.classList.remove(SCROLLING_CLASS)
      }
    } else {
      iframe.contentWindow?.postMessage({ type: 'htmlViewScrollbars', autoHide }, '*')
    }
  }

  watch(scrollbarsAutoHide, apply)

  return { apply }
}
