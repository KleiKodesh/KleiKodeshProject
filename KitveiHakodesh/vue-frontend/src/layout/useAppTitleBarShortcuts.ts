import { computed, type ComputedRef, type Ref } from 'vue'
import { useEventListener } from '@vueuse/core'
import { useAppNavigation } from '@/composables/useAppNavigation'
import { useBookViewStore } from '@/stores/bookViewStore'
import { useThemeStore } from '@/theme/themeStore'
import { toggleFullscreen } from '@/webview-host/bridge'
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
  /** Ctrl+T. Owned by the component because the address bar's visibility drives its template. */
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
    'ספרים',
    'חיפוש',
    'היברו-בוקס',
    'פתח קובץ',
    'חיפוש קבצים',
    'מילון',
    'לוח שנה',
    'מידות ושיעורים',
    'סביבות עבודה',
  ]

  // Ctrl+Tab steps through the tab list; the title bar's prev/next buttons use the
  // same pane.cycleTab, so keyboard and buttons can never disagree on the order.
  const cycleTab = pane.cycleTab

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
      return false
    }

    // Ctrl+Tab / Ctrl+Shift+Tab — one switch per physical press. Holding the combo
    // must not machine-gun through tabs via auto-repeat: each hop cold-remounts a page.
    if (e.code === 'Tab') {
      if (!e.repeat) cycleTab(e.shiftKey ? -1 : 1)
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
      case 'KeyW':
        pane.closeTab(pane.activeTabId.value)
        return true
      case 'KeyX':
        pane.closeAllTabs()
        return true
      case 'KeyB':
        if (isBookViewActive.value) bookViewStore.toggleToolbar(paneId)
        else if (activeTab.value?.route === '/pdf-view') pane.togglePdfViewerTitleBar()
        return true
      case 'KeyJ':
        if (isBookViewActive.value) bookViewStore.toggleBottomPanel(paneId)
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
        options.toggleAddressBar()
        return true
      case 'KeyN':
        pane.openNewTab()
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
    if (!e.ctrlKey) return false
    if (e.code === 'Backslash') {
      if (isSplitViewAvailable.value) bookViewStore.toggleSplitView()
      return true
    }
    if (e.shiftKey && e.code === 'KeyF') {
      toggleFullscreen()
      return true
    }
    // Swallow the browser print dialog.
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
