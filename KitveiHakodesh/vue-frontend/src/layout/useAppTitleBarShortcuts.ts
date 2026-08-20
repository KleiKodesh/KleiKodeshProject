import { computed, type ComputedRef, type Ref } from 'vue'
import { useEventListener } from '@vueuse/core'
import { useAppNavigation } from '@/composables/useAppNavigation'
import { useBookViewStore } from '@/stores/bookViewStore'
import { useThemeStore } from '@/theme/themeStore'
import { toggleFullscreen, hasNativeChromeTabs, toggleChromeTabList } from '@/webview-host/bridge'
import { toggleReadingMode } from '@/composables/useUiChromeVisibility'
import type { useAppShellPane } from '@/composables/useAppShellPane'

type Pane = ReturnType<typeof useAppShellPane>

/**
 * Every keyboard shortcut owned by the title bar, for one pane.
 *
 * Each pane installs its own handler, which splits the shortcuts in two:
 *
 *   Pane-scoped — tab operations, book-view panels, navigation within a pane.
 *     Fire only when this pane is focused (always, when split view is off).
 *   App-wide — fullscreen, split view. Handled by **pane 1 only**, so they do
 *     not fire twice while split view is open.
 *
 * Deciding which half a new shortcut belongs to is the whole point of this file:
 * an app-wide shortcut added to the pane-scoped block fires once per open pane.
 *
 * `Ctrl+1`..`Ctrl+9` and `F1` go through `navigateInNewTab` with the Hebrew
 * destination labels, so they stay in sync with the home tiles and the nav
 * dropdown — see `useAppNavigation`.
 *
 * F1 differs from `Ctrl+1`..`Ctrl+9` in where it lands, not in how it is wired:
 * settings is the one destination unique per pane, so a second F1 switches to the
 * open settings tab rather than opening another. That rule lives with the tab
 * store's `SINGLE_TAB_ROUTES`, not here.
 *
 * All matching is on `e.code`, never `e.key`: `e.key` returns the character the
 * key produces, which changes with the active keyboard language and would break
 * every shortcut the moment the user types Hebrew.
 *
 * The user-facing reference is `ShortcutsReferenceList.vue` — update it whenever
 * a binding here changes.
 */
export function useAppTitleBarShortcuts(options: {
  paneId: 1 | 2
  pane: Pane
  /** Ctrl+H toggles this. Shared per-pane ref from `useUiChromeVisibility`. */
  titleBarVisible: Ref<boolean>
  /** Ctrl+\ is ignored unless split view actually fits on screen. */
  isSplitViewAvailable: ComputedRef<boolean>
  /** Ctrl+E (and Ctrl+T where no native strip exists). Owned by the component because the
   *  address bar's visibility drives its template. */
  toggleAddressBar: () => void
  /** Ctrl+M. Owned by the component for the same reason. */
  toggleNavDropdown: () => void
}) {
  const { paneId, pane, titleBarVisible, isSplitViewAvailable } = options
  const bookViewStore = useBookViewStore()
  const themeStore = useThemeStore()
  const { navigateInNewTab } = useAppNavigation()

  const activeTab = computed(() => pane.activeTab.value)
  const isBookViewActive = computed(() => activeTab.value?.route === '/book-view')
  const isTxtViewActive = computed(() => activeTab.value?.route === '/txt-view')
  const isThisPaneFocused = computed(
    () => !bookViewStore.splitViewEnabled || bookViewStore.focusedPaneId === paneId,
  )

  /** Quick-nav destinations for Ctrl+1..Ctrl+9, in order. */
  const QUICK_NAV_LABELS = [
    'קטלוג הספרים',
    'חיפוש',
    'היברו-בוקס',
    'פתח קובץ',
    'חיפוש קבצים',
    'מילון',
    'לוח שנה',
    'מידות ושיעורים',
    'סביבות עבודה',
  ]

  // Forward Ctrl+key shortcuts from child iframes (HTML/txt viewer) back into the
  // top-level keydown pipeline. Only pane 1 needs this — iframes only appear there.
  if (paneId === 1) {
    useEventListener('message', (e: MessageEvent) => {
      if (!e.data || e.data.type !== 'iframeKeydown') return
      window.dispatchEvent(
        new KeyboardEvent('keydown', {
          code: e.data.code,
          ctrlKey: e.data.ctrlKey,
          shiftKey: e.data.shiftKey,
          metaKey: e.data.metaKey,
          altKey: e.data.altKey,
          bubbles: true,
          cancelable: true,
        }),
      )
    })
  }

  /** Returns true when the event was consumed. */
  function handlePaneScoped(e: KeyboardEvent): boolean {
    if (!e.ctrlKey) {
      if (e.code === 'F1') {
        navigateInNewTab('הגדרות')
        return true
      }
      // Alt+Arrow — the tab's own Back/Forward, like a browser. RTL: back is the
      // RIGHT arrow, matching the title-bar button icons. Ctrl+Arrow stays with
      // the book view's section navigation, which is why the modifier is Alt.
      // stopPropagation too: plain-arrow handlers (tile grids, list navigation)
      // don't check modifiers, and this must not also move their focus.
      if (e.altKey && (e.code === 'ArrowRight' || e.code === 'ArrowLeft')) {
        e.stopPropagation()
        if (e.code === 'ArrowRight') pane.goBack()
        else pane.goForward()
        return true
      }
      return false
    }

    // Ctrl+Tab / Ctrl+Shift+Tab.
    //
    // With a native strip, this opens the strip's tab-list dropdown as an Alt+Tab-style
    // switcher: it stays up while Ctrl is held, each further Tab steps the highlight, and
    // releasing Ctrl activates the highlighted tab. Nothing loads until release, so
    // stepping past five tabs costs one remount instead of five.
    //
    // The page cannot drive that hold — the native popup takes OS focus as it opens, so no
    // keyup ever arrives here. We hand the direction to C# and it owns the rest, including
    // the repeat presses (which reach the focused popup, not this handler).
    //
    // Everywhere else there is exactly one tab by construction, so the same keys walk the
    // tab's own history instead — the same thing the title bar's back/forward arrows do.
    if (e.code === 'Tab') {
      if (e.repeat) return true
      if (hasNativeChromeTabs) toggleChromeTabList(e.shiftKey ? -1 : 1)
      else if (e.shiftKey) pane.goForward()
      else pane.goBack()
      return true
    }

    // Ctrl+1..Ctrl+9 — quick-nav in a new tab.
    if (e.code.startsWith('Digit')) {
      const index = Number(e.code.slice(5)) - 1
      const label = QUICK_NAV_LABELS[index]
      if (label === undefined) return false
      navigateInNewTab(label)
      return true
    }

    switch (e.code) {
      // Closing is meaningless where there is only ever one tab — the store would just
      // replace it with a fresh home tab, which reads as "my document vanished".
      case 'KeyW':
        if (hasNativeChromeTabs) pane.closeTab(pane.activeTabId.value)
        return true
      case 'KeyX':
        if (hasNativeChromeTabs) pane.closeAllTabs()
        return true
      case 'KeyB':
        if (isBookViewActive.value) bookViewStore.toggleToolbar(paneId)
        else if (activeTab.value?.route === '/pdf-view') pane.togglePdfViewerTitleBar()
        return true
      case 'KeyJ':
        // Ctrl+J toggles the bottom commentary panel, Ctrl+Shift+J the right-side
        // one, Ctrl+Alt+J the left-side one.
        if (isBookViewActive.value) {
          const slot = e.altKey ? 'side-left' : e.shiftKey ? 'side' : 'bottom'
          bookViewStore.toggleCommentaryPanel(paneId, slot)
        }
        return true
      case 'KeyK':
        if (isBookViewActive.value) bookViewStore.toggleTocPanel(paneId)
        return true
      case 'KeyF':
        // Let a focused search input keep its own Ctrl+F.
        if (document.activeElement?.closest('[data-ctrlf-enabled]')) return false
        if (isBookViewActive.value) bookViewStore.openSearch(paneId)
        else if (isTxtViewActive.value) bookViewStore.txtViewToggleSearch(paneId)
        return true
      case 'KeyT':
        // Ctrl+T is the tab list, and the native strip is the only thing that has one.
        // It used to fall back to the address bar where no strip exists, from when that
        // dropdown listed open tabs — it lists recent locations now, and Ctrl+E already
        // opens it, so the fallback was just a second key for the same thing.
        if (hasNativeChromeTabs) toggleChromeTabList()
        return true
      case 'KeyE':
        options.toggleAddressBar()
        return true
      // Only the demo-app host has somewhere to put a new tab. The VSTO task pane
      // shows one tab with no strip, and the dev browser has no strip either — in
      // both, Ctrl+N would open a tab the user can never navigate back to.
      case 'KeyN':
        if (hasNativeChromeTabs) pane.openNewTab()
        return true
      case 'KeyG':
        pane.goHome()
        return true
      case 'KeyH':
        titleBarVisible.value = !titleBarVisible.value
        return true
      case 'KeyL':
        themeStore.toggleDarkMode()
        return true
      case 'KeyM':
        options.toggleNavDropdown()
        return true
      default:
        return false
    }
  }

  /** Returns true when the event was consumed. Pane 1 only. */
  function handleAppWide(e: KeyboardEvent): boolean {
    if (e.code === 'F11') {
      toggleFullscreen()
      return true
    }
    // F9 — reading mode: hide/show all chrome at once (title bars and book
    // toolbars). Check-all semantics live in `toggleReadingMode`.
    // F9 is Firefox's Reader View key — the closest convention for this — and
    // has no default in Chromium/WebView2, so there is nothing to suppress.
    if (e.code === 'F9') {
      toggleReadingMode()
      return true
    }
    if (!e.ctrlKey) return false
    if (e.code === 'Backslash') {
      if (isSplitViewAvailable.value) bookViewStore.toggleSplitView()
      return true
    }
    if (e.shiftKey && e.code === 'KeyF') {
      toggleFullscreen()
      return true
    }
    // Swallow the browser print dialog. This is preventDefault-only (no
    // stopPropagation), and `usePdfPrintShortcut` relies on that: on the PDF
    // page it still sees this keydown and forwards the print into the PDF.js
    // iframe.
    if (e.code === 'KeyP') return true
    return false
  }

  useEventListener(
    'keydown',
    (e: KeyboardEvent) => {
      if (isThisPaneFocused.value && handlePaneScoped(e)) {
        e.preventDefault()
        return
      }
      if (paneId === 1 && handleAppWide(e)) e.preventDefault()
    },
    { capture: true },
  )
}
