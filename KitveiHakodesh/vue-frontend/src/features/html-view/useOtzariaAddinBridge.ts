/**
 * Otzaria addin bridge for HtmlViewPage.
 *
 * When an HTML file is opened and its tab has isOtzariaAddin=true, HtmlViewPage
 * calls `onIframeLoaded()` from this composable after the iframe fires onload.
 * This makes sure the official `window.Otzaria` SDK stub exists in the iframe —
 * injected here in dev (same-origin /khs-file proxy), pre-injected by C# in hosted
 * mode (cross-origin kitvei-localhtml-N host, JsBridge.OtzariaAddinBridgeStubScript)
 * — fires the plugin.boot event, and routes every stub call back to the Vue app via
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
  /**
   * Hosted mode pre-injects the stub into EVERY kitvei-localhtml frame — plain local
   * HTML pages included, since C# cannot know at document creation which frame is an
   * addin. This flag is the actual gate: calls are only answered for tabs flagged
   * isOtzariaAddin, so on a plain page the stub stays inert.
   */
  isOtzariaAddinRef: Ref<boolean>,
) {
  function pushEvent(event: string, payload: unknown) {
    iframeRef.value?.contentWindow?.postMessage({ type: 'otzaria-event', event, payload }, '*')
  }

  // ── Message handler ─────────────────────────────────────────────────────────

  async function handleMessage(event: MessageEvent) {
    if (!event.data || event.data.type !== 'otzaria-call') return
    if (!isOtzariaAddinRef.value) return
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
  let bootTimer: number | null = null

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
    if (bootTimer !== null) {
      clearTimeout(bootTimer)
      bootTimer = null
    }
    themeObserver?.disconnect()
    themeObserver = null
  })

  return {
    /** Call once after the iframe's onload fires to inject the stub and fire plugin.boot. */
    onIframeLoaded() {
      const iframe = iframeRef.value
      if (!iframe?.contentWindow) return

      // The stub is normally pre-injected before the addin's own scripts run —
      // addins that touch window.Otzaria at startup need it to already exist:
      //  • Hosted: C# injects it on document creation (the kitvei-localhtml-N frame
      //    is cross-origin, contentDocument is null here, and injection from this
      //    side is impossible anyway) — see JsBridge.OtzariaAddinBridgeStubScript.
      //  • Dev: the same-origin /khs-file vite proxy inserts it into the served HTML
      //    (injectAddinBridgeStub in vite.config.ts).
      // The injection below is only the dev fallback for HTML the proxy could not
      // touch (UTF-16/32 documents). An inaccessible document means "already
      // handled", never "give up": plugin.boot must still fire below.
      try {
        const iframeDocument = iframe.contentDocument
        if (iframeDocument) {
          const script = iframeDocument.createElement('script')
          script.textContent = buildBridgeStubScript()
          iframeDocument.head?.appendChild(script) ?? iframeDocument.body?.appendChild(script)
        }
      } catch {
        // Engines that throw on cross-origin contentDocument instead of returning
        // null — same meaning as the null case: the pre-injected stub owns this frame.
      }

      // Wait one tick for the stub to be evaluated, then fire plugin.boot.
      // Tracked so unmount inside this window can't start a theme observer
      // after onBeforeUnmount already disconnected it (leak).
      bootTimer = window.setTimeout(() => {
        bootTimer = null
        startThemeObserver()
        pushEvent('plugin.boot', buildBootPayload(addinIdRef.value))
      }, 50)
    },
  }
}
