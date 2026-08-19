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
 * App-wide, persisted — the scrollbars mode: static (always visible) or
 * Windows-11-style auto-hide (transparent when idle, visible while scrolling
 * plus a short linger). The value lives in settingsStore
 * ('app.scrollbarsAutoHide', settings-page control included); this module owns
 * only the DOM effect. Keyboard shortcut: Ctrl+Shift+H — handled in the
 * app-wide block of `useAppTitleBarShortcuts`.
 */

const pane1TitleBarVisible = ref(true)
const pane2TitleBarVisible = ref(true)

/**
 * The auto-hide DOM effect: `auto-hide-scrollbars` on the root element is the
 * mode, `scrollbars-scrolling` is live scroll activity (any scroll anywhere,
 * caught by a capture listener, lingering for a moment after the last event).
 * The CSS lives in `main.css` — bars fade by color only, so the gutter stays
 * put and toggling never causes layout shift. The classes cannot reach into
 * iframe documents (HTML/txt viewer, PDF.js) — pages owning an iframe
 * propagate the mode into it with `useIframeScrollbarsAutoHide`.
 */
const SCROLLBARS_SCROLLING_LINGER_MS = 1000
let scrollbarsScrollingTimer: number | null = null

function onAnyScroll() {
  document.documentElement.classList.add('scrollbars-scrolling')
  if (scrollbarsScrollingTimer !== null) clearTimeout(scrollbarsScrollingTimer)
  scrollbarsScrollingTimer = window.setTimeout(() => {
    scrollbarsScrollingTimer = null
    document.documentElement.classList.remove('scrollbars-scrolling')
  }, SCROLLBARS_SCROLLING_LINGER_MS)
}

function applyScrollbarsAutoHide(autoHide: boolean) {
  const root = document.documentElement
  root.classList.toggle('auto-hide-scrollbars', autoHide)
  if (autoHide) {
    // Re-adding an identical listener is a no-op, so this is idempotent.
    window.addEventListener('scroll', onAnyScroll, { capture: true, passive: true })
  } else {
    window.removeEventListener('scroll', onAnyScroll, { capture: true })
    root.classList.remove('scrollbars-scrolling')
    if (scrollbarsScrollingTimer !== null) {
      clearTimeout(scrollbarsScrollingTimer)
      scrollbarsScrollingTimer = null
    }
  }
}

// The watcher runs in a detached effect scope so the component that happens to
// create it first (AppTitleBar, mounted for the app's whole lifetime anyway)
// cannot take it down. Guarded so it starts exactly once.
let scrollbarsEffectStarted = false

function ensureScrollbarsAutoHideEffect() {
  if (scrollbarsEffectStarted) return
  scrollbarsEffectStarted = true
  const settingsStore = useSettingsStore()
  effectScope(true).run(() => {
    watch(() => settingsStore.scrollbarsAutoHide, applyScrollbarsAutoHide, { immediate: true })
  })
}

export function toggleScrollbarsAutoHide() {
  ensureScrollbarsAutoHideEffect()
  const settingsStore = useSettingsStore()
  settingsStore.scrollbarsAutoHide = !settingsStore.scrollbarsAutoHide
}

export function useUiChromeVisibility(paneId: 1 | 2 = 1) {
  ensureScrollbarsAutoHideEffect()
  const { scrollbarsAutoHide } = storeToRefs(useSettingsStore())
  return {
    titleBarVisible: paneId === 1 ? pane1TitleBarVisible : pane2TitleBarVisible,
    scrollbarsAutoHide,
  }
}

/**
 * Reading mode — F9, handled in the app-wide block of `useAppTitleBarShortcuts`.
 *
 * A "check all / uncheck all" over the hideable chrome: title bars (Ctrl+H),
 * book-view toolbars (Ctrl+B) and scrollbars auto-hide (Ctrl+Shift+H). There is
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
    settingsStore.scrollbarsAutoHide &&
    panesOnScreen.every(
      (paneId) => !titleBarByPane[paneId].value && !bookViewStore.getToolbarVisible(paneId),
    )
  const hideAll = !everythingHidden

  if (settingsStore.scrollbarsAutoHide !== hideAll) toggleScrollbarsAutoHide()
  for (const paneId of [1, 2] as const) {
    titleBarByPane[paneId].value = !hideAll
    if (bookViewStore.getToolbarVisible(paneId) === hideAll) bookViewStore.toggleToolbar(paneId)
  }
}
