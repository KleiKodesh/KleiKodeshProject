import { watch } from 'vue'
import { useUiChromeVisibility } from './useUiChromeVisibility'

const HIDE_STYLE_ID = '__kitvei-hide-scrollbars'
// !important so it beats the thin-scrollbar style the C#-injected
// IframeScrollScript re-adds on every theme message.
const HIDE_STYLE_CSS =
  '* { scrollbar-width: none !important; } *::-webkit-scrollbar { display: none !important; }'

/**
 * Keeps one iframe's scrollbars in sync with the app-wide hidden state
 * (Ctrl+Shift+H, and F9 reading mode) so framed documents behave like an
 * integral part of the app — the `hide-scrollbars` class on the app root
 * cannot reach into another document.
 *
 * Two delivery paths, chosen per call by whether the frame's document is
 * reachable (`contentDocument` is null for cross-origin frames):
 *   - Same-origin (the PDF.js viewer, and local files in browser dev mode):
 *     a style element injected straight into the frame's document.
 *   - Cross-origin (local files on kitvei-localfile-N in the WebView2 host):
 *     a { type: 'htmlViewScrollbars', hidden } postMessage, applied inside the
 *     frame by the C#-injected IframeScrollScript (JsBridge.cs) — the same
 *     channel theme sync and scroll restore use.
 *
 * The owning page must call `apply` from the iframe's load handler: a freshly
 * loaded document always starts with visible scrollbars, whatever the app
 * state. State changes while the frame is alive are pushed by the watcher.
 */
export function useIframeScrollbarsHidden(getIframe: () => HTMLIFrameElement | null) {
  const { scrollbarsHidden } = useUiChromeVisibility()

  function apply() {
    const iframe = getIframe()
    if (!iframe) return
    const hidden = scrollbarsHidden.value
    const doc = iframe.contentDocument
    if (doc) {
      const existing = doc.getElementById(HIDE_STYLE_ID)
      if (hidden && !existing) {
        const style = doc.createElement('style')
        style.id = HIDE_STYLE_ID
        style.textContent = HIDE_STYLE_CSS
        ;(doc.head ?? doc.documentElement)?.appendChild(style)
      } else if (!hidden && existing) {
        existing.remove()
      }
    } else {
      iframe.contentWindow?.postMessage({ type: 'htmlViewScrollbars', hidden }, '*')
    }
  }

  watch(scrollbarsHidden, apply)

  return { apply }
}
