import { effectScope, ref, watch } from 'vue'
import { storeToRefs } from 'pinia'
import { useBookViewStore } from '@/stores/bookViewStore'
import { useSettingsStore } from '@/stores/settingsStore'

/**
 * UI chrome visibility, in two halves:
 *
 * Per-pane, session-only — each pane has its own independent titleBarVisible
 * ref so Ctrl+H in one pane does not affect the other pane's title bar.
 * Resets to defaults on page reload. Handled in AppTitleBar's pane-scoped
 * keydown block.
 *
 * App-wide, persisted — hidden scrollbars: completely invisible except while
 * actually scrolling. The value lives in settingsStore
 * ('app.scrollbarsHidden', settings-page control included); this module owns
 * only the DOM effect. Keyboard shortcut: Ctrl+Shift+H — handled in the
 * app-wide block of `useAppTitleBarShortcuts`.
 */

const pane1TitleBarVisible = ref(true)
const pane2TitleBarVisible = ref(true)

/**
 * The hidden-scrollbars DOM effect, pure CSS tinting: `scrollbars-hidden` on
 * the root makes every bar transparent; `scrollbars-scrolling` sits on the ONE
 * element that actually scrolled (caught by a capture listener, lingering for
 * a moment after the last event), so only its bar is revealed — never every
 * scrollbar in the app at once. CSS in `main.css`; only colors change, so
 * toggling never causes layout shift.
 *
 * The classes cannot reach into iframe documents (the HTML viewer and
 * PDF.js) — pages owning an iframe propagate the state into it with
 * `useIframeScrollbarsHidden`.
 */
const SCROLLBARS_SCROLLING_LINGER_MS = 1000
let scrollbarsScrollingTimer: number | null = null
let scrollingElement: Element | null = null

function clearScrollingFlag() {
  scrollingElement?.classList.remove('scrollbars-scrolling')
  scrollingElement = null
  if (scrollbarsScrollingTimer !== null) {
    clearTimeout(scrollbarsScrollingTimer)
    scrollbarsScrollingTimer = null
  }
}

function onAnyScroll(event: Event) {
  const target = event.target instanceof Element ? event.target : document.documentElement
  if (scrollingElement !== target) {
    scrollingElement?.classList.remove('scrollbars-scrolling')
    scrollingElement = target
  }
  // Unconditionally: a Vue :class re-patch overwrites the whole class attribute
  // mid-scroll, and re-adding on every event (idempotent, cheap) self-heals.
  target.classList.add('scrollbars-scrolling')
  if (scrollbarsScrollingTimer !== null) clearTimeout(scrollbarsScrollingTimer)
  scrollbarsScrollingTimer = window.setTimeout(() => {
    scrollbarsScrollingTimer = null
    clearScrollingFlag()
  }, SCROLLBARS_SCROLLING_LINGER_MS)
}

function applyScrollbarsHidden(hidden: boolean) {
  document.documentElement.classList.toggle('scrollbars-hidden', hidden)
  if (hidden) {
    // Re-adding an identical listener is a no-op, so this is idempotent.
    window.addEventListener('scroll', onAnyScroll, { capture: true, passive: true })
  } else {
    window.removeEventListener('scroll', onAnyScroll, { capture: true })
    clearScrollingFlag()
  }
}

// The watcher runs in a detached effect scope so the component that happens to
// create it first (AppTitleBar, mounted for the app's whole lifetime anyway)
// cannot take it down. Guarded so it starts exactly once.
let scrollbarsEffectStarted = false

function ensureScrollbarsHiddenEffect() {
  if (scrollbarsEffectStarted) return
  scrollbarsEffectStarted = true
  const settingsStore = useSettingsStore()
  effectScope(true).run(() => {
    watch(() => settingsStore.scrollbarsHidden, applyScrollbarsHidden, { immediate: true })
  })
}

export function toggleScrollbarsHidden() {
  ensureScrollbarsHiddenEffect()
  const settingsStore = useSettingsStore()
  settingsStore.scrollbarsHidden = !settingsStore.scrollbarsHidden
}

export function useUiChromeVisibility(paneId: 1 | 2 = 1) {
  ensureScrollbarsHiddenEffect()
  const { scrollbarsHidden } = storeToRefs(useSettingsStore())
  return {
    titleBarVisible: paneId === 1 ? pane1TitleBarVisible : pane2TitleBarVisible,
    scrollbarsHidden,
  }
}

// ── Iframe propagation ───────────────────────────────────────────────────────

const IFRAME_HIDDEN_STYLE_ID = '__kitvei-hidden-scrollbars'
const IFRAME_SCROLLING_CLASS = '__kitvei-scrollbars-scrolling'
// !important so it beats the thin-scrollbar style the C#-injected
// IframeScrollScript re-adds on every theme message.
// The bare html selector matters: the frame's viewport scrollbar styles come
// from the root element itself, which `html *` alone would miss.
const IFRAME_HIDDEN_STYLE_CSS = `html:not(.${IFRAME_SCROLLING_CLASS}), html:not(.${IFRAME_SCROLLING_CLASS}) * { scrollbar-color: transparent transparent !important; }`

/**
 * Keeps one iframe following the app-wide hidden-scrollbars setting — the
 * classes above cannot reach into another document, so framed pages (the HTML
 * viewer, PDF.js) would otherwise keep their bars.
 *
 * Two delivery paths, chosen per call by whether the frame's document is
 * reachable (`contentDocument` is null for cross-origin frames):
 *   - Same-origin (the PDF.js viewer, and local files in browser dev mode):
 *     a style element injected straight into the frame's document, plus a
 *     capture scroll listener on the frame's window that flags scroll activity
 *     on the frame's own root element — the per-frame analogue of the
 *     per-element tracking above.
 *   - Cross-origin (local files on kitvei-localfile-N in the WebView2 host):
 *     a { type: 'htmlViewScrollbars', hidden } postMessage, applied inside the
 *     frame by the C#-injected IframeScrollScript (JsBridge.cs), which tracks
 *     its own scroll activity — the same channel theme sync and scroll restore
 *     use.
 *
 * The owning page must call `apply` from the iframe's load handler: a freshly
 * loaded document starts unhidden, whatever the app state, and navigation
 * discards the previous document's style and listener. State changes while the
 * frame is alive are pushed by the watcher.
 */
export function useIframeScrollbarsHidden(getIframe: () => HTMLIFrameElement | null) {
  ensureScrollbarsHiddenEffect()
  const { scrollbarsHidden } = storeToRefs(useSettingsStore())

  let scrollingTimer: number | null = null

  function onFrameScroll() {
    const frameRoot = getIframe()?.contentDocument?.documentElement
    if (!frameRoot) return
    frameRoot.classList.add(IFRAME_SCROLLING_CLASS)
    if (scrollingTimer !== null) clearTimeout(scrollingTimer)
    scrollingTimer = window.setTimeout(() => {
      scrollingTimer = null
      getIframe()?.contentDocument?.documentElement.classList.remove(IFRAME_SCROLLING_CLASS)
    }, SCROLLBARS_SCROLLING_LINGER_MS)
  }

  function apply() {
    const iframe = getIframe()
    if (!iframe) return
    const hidden = scrollbarsHidden.value
    const doc = iframe.contentDocument
    if (doc) {
      const existing = doc.getElementById(IFRAME_HIDDEN_STYLE_ID)
      if (hidden && !existing) {
        const style = doc.createElement('style')
        style.id = IFRAME_HIDDEN_STYLE_ID
        style.textContent = IFRAME_HIDDEN_STYLE_CSS
        ;(doc.head ?? doc.documentElement)?.appendChild(style)
      } else if (!hidden && existing) {
        existing.remove()
      }
      // The listener dies with the frame's inner window on navigation and
      // re-adding an identical one is a no-op, so both branches are idempotent.
      if (hidden) {
        iframe.contentWindow?.addEventListener('scroll', onFrameScroll, {
          capture: true,
          passive: true,
        })
      } else {
        iframe.contentWindow?.removeEventListener('scroll', onFrameScroll, { capture: true })
        doc.documentElement.classList.remove(IFRAME_SCROLLING_CLASS)
      }
    } else {
      iframe.contentWindow?.postMessage({ type: 'htmlViewScrollbars', hidden }, '*')
    }
  }

  watch(scrollbarsHidden, apply)

  return { apply }
}

/**
 * Reading mode — F9, handled in the app-wide block of `useAppTitleBarShortcuts`.
 *
 * A "check all / uncheck all" over the hideable chrome: title bars (Ctrl+H),
 * book-view toolbars (Ctrl+B) and hidden scrollbars (Ctrl+Shift+H). There is
 * no stored reading-mode flag — whether it is "on" is derived from the
 * individual states, so the individual shortcuts keep working and F9 never
 * fights them: if anything is still visible, F9 hides everything; only when
 * everything is already hidden does it bring everything back.
 *
 * Derivation only looks at panes that are on screen (pane 2 counts only in
 * split view), but applying sets both panes so a pane opened later matches.
 * Toolbar visibility goes through the store's own toggle, keeping its
 * persistence semantics identical to pressing Ctrl+B.
 */
export function toggleReadingMode() {
  const settingsStore = useSettingsStore()
  const bookViewStore = useBookViewStore()
  const titleBarByPane = { 1: pane1TitleBarVisible, 2: pane2TitleBarVisible } as const
  const panesOnScreen: readonly (1 | 2)[] = bookViewStore.splitViewEnabled ? [1, 2] : [1]

  const everythingHidden =
    settingsStore.scrollbarsHidden &&
    panesOnScreen.every(
      (paneId) => !titleBarByPane[paneId].value && !bookViewStore.getToolbarVisible(paneId),
    )
  const hideAll = !everythingHidden

  if (settingsStore.scrollbarsHidden !== hideAll) toggleScrollbarsHidden()
  for (const paneId of [1, 2] as const) {
    titleBarByPane[paneId].value = !hideAll
    if (bookViewStore.getToolbarVisible(paneId) === hideAll) bookViewStore.toggleToolbar(paneId)
  }
}
