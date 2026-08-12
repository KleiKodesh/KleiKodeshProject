/**
 * Otzaria addin bridge for HtmlViewPage.
 *
 * When an HTML file is opened and its tab has isOtzariaAddin=true, HtmlViewPage
 * calls `onIframeLoaded()` from this composable after the iframe fires onload.
 * This injects the official `window.Otzaria` SDK stub into the iframe
 * (otzariaAddinBridgeStub.ts) and routes every call back to the Vue app via
 * postMessage, answering with the official `{ success, data, error }` envelope.
 *
 * Only data-query APIs are served — see otzariaAddinDataQueryApi.ts for the
 * allowlist and the PERMISSION_DENIED policy for everything else.
 *
 * The bridge is self-contained: it sets up and tears down its own window.message
 * listener in onMounted/onBeforeUnmount, so HtmlViewPage just calls onIframeLoaded()
 * once and everything else is automatic.
 */

import { onMounted, onBeforeUnmount, type Ref } from 'vue'
import { buildBridgeStubScript } from './otzariaAddinBridgeStub'
import {
  routeDataQueryCall,
  buildAddinTheme,
  buildBootPayload,
  OtzariaBridgeError,
} from './otzariaAddinDataQueryApi'

export function useOtzariaAddinBridge(
  iframeRef: Ref<HTMLIFrameElement | null>,
  /** The addin's plugin-folder name (used to scope its sandboxed storage). */
  addinIdRef: Ref<string>,
) {
  function pushEvent(event: string, payload: unknown) {
    iframeRef.value?.contentWindow?.postMessage({ type: 'otzaria-event', event, payload }, '*')
  }

  // ── Message handler ─────────────────────────────────────────────────────────

  async function handleMessage(event: MessageEvent) {
    if (!event.data || event.data.type !== 'otzaria-call') return
    const iframe = iframeRef.value
    if (!iframe?.contentWindow || event.source !== iframe.contentWindow) return

    const { callId, method, params } = event.data
    let reply: { success: boolean; data: unknown; error: unknown }
    try {
      const data = await routeDataQueryCall(String(method), params, addinIdRef.value)
      reply = { success: true, data, error: null }
    } catch (thrown) {
      const code = thrown instanceof OtzariaBridgeError ? thrown.code : 'INTERNAL'
      const message = thrown instanceof Error ? thrown.message : String(thrown)
      reply = { success: false, data: null, error: { code, message, schemaVersion: 1 } }
    }

    iframe.contentWindow.postMessage({ type: 'otzaria-reply', callId, ...reply }, '*')
  }

  // ── Theme observer ──────────────────────────────────────────────────────────

  let themeObserver: MutationObserver | null = null

  function startThemeObserver() {
    themeObserver?.disconnect()
    themeObserver = new MutationObserver(() => pushEvent('theme.changed', buildAddinTheme()))
    themeObserver.observe(document.documentElement, {
      attributes: true,
      attributeFilter: ['data-theme-preset', 'style'],
    })
  }

  // ── Lifecycle ───────────────────────────────────────────────────────────────

  onMounted(() => window.addEventListener('message', handleMessage))

  onBeforeUnmount(() => {
    window.removeEventListener('message', handleMessage)
    themeObserver?.disconnect()
    themeObserver = null
  })

  return {
    /** Call once after the iframe's onload fires to inject the stub and fire plugin.boot. */
    onIframeLoaded() {
      const iframe = iframeRef.value
      if (!iframe?.contentWindow || !iframe?.contentDocument) return

      // Inject the bridge stub as a <script> tag directly into the iframe document.
      // This works because the virtual host serves the addin on the same origin as the
      // Vue app (same-origin /khs-file proxy in dev; ms-local-stream in hosted mode also
      // allows same-document script injection via contentDocument).
      try {
        const script = iframe.contentDocument.createElement('script')
        script.textContent = buildBridgeStubScript()
        iframe.contentDocument.head?.appendChild(script) ?? iframe.contentDocument.body?.appendChild(script)
      } catch {
        // Cross-origin fallback — should not happen with virtual host URLs, but be defensive.
        return
      }

      // Wait one tick for the stub to be evaluated, then fire plugin.boot.
      setTimeout(() => {
        startThemeObserver()
        pushEvent('plugin.boot', buildBootPayload(addinIdRef.value))
      }, 50)
    },
  }
}
