import { watch } from 'vue'
import { useUiChromeVisibility } from './useUiChromeVisibility'
import { hasNativeFluentScrollbars } from '@/webview-host/bridge'

const AUTO_HIDE_STYLE_ID = '__kitvei-auto-hide-scrollbars'
const NATIVE_STYLE_ID = '__kitvei-native-scrollbars'
const SCROLLING_CLASS = '__kitvei-scrollbars-scrolling'
// !important so it beats the thin-scrollbar style the C#-injected
// IframeScrollScript re-adds on every theme message.
// The bare html selector matters: the frame's viewport scrollbar styles come
// from the root element itself, which `html *` alone would miss.
const AUTO_HIDE_STYLE_CSS = `html:not(.${SCROLLING_CLASS}), html:not(.${SCROLLING_CLASS}) * { scrollbar-color: transparent transparent !important; }`
// Native mode: the WebView2 environment renders fluent overlay bars, but only
// where NO author scrollbar styling applies — this resets whatever the frame's
// own stylesheets set (PDF.js viewer-custom.css, the injected theme style).
const NATIVE_STYLE_CSS =
  'html, html * { scrollbar-color: auto !important; scrollbar-width: auto !important; }'
const SCROLLING_LINGER_MS = 1000

/**
 * Keeps one iframe following the app-wide scrollbars mode — static or
 * Windows-11-style auto-hide (Ctrl+Shift+H, F9 reading mode, or the settings
 * page) — so framed documents behave like an integral part of the app: the
 * classes on the app root cannot reach into another document.
 *
 * Mirrors `useUiChromeVisibility`'s environment split: in the WebView2 host,
 * auto-hide means clearing the frame's author scrollbar styling so the
 * environment's native fluent overlay bars show; in the dev browser it means
 * the CSS emulation (transparent at rest, revealed by a scroll-activity class).
 *
 * Two delivery paths, chosen per call by whether the frame's document is
 * reachable (`contentDocument` is null for cross-origin frames):
 *   - Same-origin (the PDF.js viewer, and local files in browser dev mode):
 *     a style element injected straight into the frame's document, plus — in
 *     emulation mode — a capture scroll listener on the frame's window that
 *     flags scroll activity on the frame's own root element.
 *   - Cross-origin (local files on kitvei-localfile-N in the WebView2 host):
 *     a { type: 'htmlViewScrollbars', autoHide, native } postMessage, applied
 *     inside the frame by the C#-injected IframeScrollScript (JsBridge.cs) —
 *     the same channel theme sync and scroll restore use.
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

  function setInjectedStyle(doc: Document, id: string, css: string | null) {
    const existing = doc.getElementById(id)
    if (css && !existing) {
      const style = doc.createElement('style')
      style.id = id
      style.textContent = css
      ;(doc.head ?? doc.documentElement)?.appendChild(style)
    } else if (!css && existing) {
      existing.remove()
    }
  }

  function apply() {
    const iframe = getIframe()
    if (!iframe) return
    const autoHide = scrollbarsAutoHide.value
    const native = autoHide && hasNativeFluentScrollbars
    const doc = iframe.contentDocument
    if (doc) {
      setInjectedStyle(doc, NATIVE_STYLE_ID, native ? NATIVE_STYLE_CSS : null)
      setInjectedStyle(doc, AUTO_HIDE_STYLE_ID, autoHide && !native ? AUTO_HIDE_STYLE_CSS : null)
      // The listener dies with the frame's inner window on navigation and
      // re-adding an identical one is a no-op, so both branches are idempotent.
      if (autoHide && !native) {
        iframe.contentWindow?.addEventListener('scroll', onFrameScroll, {
          capture: true,
          passive: true,
        })
      } else {
        iframe.contentWindow?.removeEventListener('scroll', onFrameScroll, { capture: true })
        doc.documentElement.classList.remove(SCROLLING_CLASS)
      }
    } else {
      iframe.contentWindow?.postMessage({ type: 'htmlViewScrollbars', autoHide, native }, '*')
    }
  }

  watch(scrollbarsAutoHide, apply)

  return { apply }
}
