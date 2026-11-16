import { computed, type ComputedRef, type Ref } from 'vue'
import { sendFastDocViewCommand } from '@/composables/fastDocViewCommands'
import { useEventListener } from '@vueuse/core'
import { useAppNavigation } from '@/composables/useAppNavigation'
import { useBookViewStore } from '@/stores/bookViewStore'
import { useSettingsStore } from '@/stores/settingsStore'
import { useThemeStore } from '@/theme/themeStore'
import { toggleFullscreen, hasNativeChromeTabs, toggleChromeTabList } from '@/webview-host/bridge'
import { toggleReadingMode, toggleScrollbarsHidden } from '@/composables/useUiChromeVisibility'
import { HAMESHIV_LABEL } from '@/layout/appNavItems'
import { showToast } from '@/composables/useToast'
import { t } from '@/i18n'
import { isPersonalId, useShortcutsStore } from '@/shortcuts/shortcutsStore'
import { useBooksDataStore } from '@/stores/booksDataStore'
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
 * Which key does what is NOT decided here any more: the combinations live in
 * `src/shortcuts/shortcutRegistry.ts` (defaults) and the shortcuts store (the user's
 * changes, from the settings grid). This file asks the store which shortcut id a keydown
 * is and runs the action for that id. A new shortcut is a registry row plus a case here.
 * Only Ctrl+Tab (driven by the native strip) and the browser-default guards (Ctrl+N,
 * Ctrl+T without a strip, Ctrl+P) still match keys directly.
 *
 * All matching is on `e.code`, never `e.key`: `e.key` returns the character the
 * key produces, which changes with the active keyboard language and would break
 * every shortcut the moment the user types Hebrew.
 */
export function useAppTitleBarShortcuts(options: {
  paneId: 1 | 2
  pane: Pane
  /** view.appBar (Ctrl+H by default) toggles this. Shared per-pane ref from `useUiChromeVisibility`. */
  titleBarVisible: Ref<boolean>
  /** view.splitView (Ctrl+\ by default) is ignored unless split view actually fits on screen. */
  isSplitViewAvailable: ComputedRef<boolean>
  /** nav.addressBar (Ctrl+E). Owned by the component because the
   *  address bar's visibility drives its template. */
  toggleAddressBar: () => void
  /** nav.mainMenu (Ctrl+M). Owned by the component for the same reason. */
  toggleNavDropdown: () => void
  /** nav.workspaces (F8). Owned by the component, which holds the picker's open state. */
  toggleWorkspaces: () => void
}) {
  const { paneId, pane, titleBarVisible, isSplitViewAvailable } = options
  const bookViewStore = useBookViewStore()
  const settingsStore = useSettingsStore()
  const themeStore = useThemeStore()
  const shortcuts = useShortcutsStore()
  const booksData = useBooksDataStore()
  const { navigateInNewTab } = useAppNavigation()

  const activeTab = computed(() => pane.activeTab.value)
  const isBookViewActive = computed(() => activeTab.value?.route === '/book-view')
  const isFastDocViewActive = computed(() => activeTab.value?.route === '/fast-doc-view')
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
    HAMESHIV_LABEL,
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

  /** Quick-nav ids, in the same order as QUICK_NAV_LABELS. */
  const QUICK_NAV_IDS = [
    'go.catalog', 'go.search', 'go.hebrewBooks', 'go.openFile', 'go.fileSearch',
    'go.dictionary', 'go.calendar', 'go.measures', 'go.hameshiv',
  ]

  /**
   * One pane-scoped shortcut. Returns true when this handler owns the id (and so the
   * keydown is consumed), false for ids another handler acts on (the book view's own
   * Ctrl+D, section keys, ...).
   *
   * Book-view and document ids are consumed even when that page is not the active one:
   * Ctrl+F and friends must not fall through to the browser's own find bar elsewhere.
   */
  function runPaneShortcut(id: string, e: KeyboardEvent): boolean {
    const quickLabel = QUICK_NAV_LABELS[QUICK_NAV_IDS.indexOf(id)]
    if (quickLabel !== undefined) {
      navigateInNewTab(quickLabel)
      return true
    }
    switch (id) {
      case 'go.settings':
        navigateInNewTab('הגדרות')
        return true
      case 'go.userSpace':
        navigateInNewTab('אזור אישי')
        return true
      // F8 by default: a bare function key rather than a Ctrl pair because every Ctrl+letter
      // left is one the BROWSER takes before the page ever sees it (Ctrl+Shift+W closes the
      // window outright). F8 has no Chromium binding at all.
      case 'nav.workspaces':
        options.toggleWorkspaces()
        return true
      // The tab's own Back/Forward, like a browser. stopPropagation too: plain-arrow handlers
      // (tile grids, list navigation) don't check modifiers, and Alt+Arrow must not also move
      // their focus.
      case 'nav.back':
        e.stopPropagation()
        pane.goBack()
        return true
      case 'nav.forward':
        e.stopPropagation()
        pane.goForward()
        return true
      // History-aware close for the native-strip host, where tabs.close owns the real tab
      // close: remove the current page (or all pages) from both history stacks.
      case 'pages.close':
        pane.closeCurrentPage()
        return true
      case 'pages.closeAll':
        pane.closeAllPages()
        return true
      // Closing is meaningless where there is only ever one tab - the store would just
      // replace it with a fresh home tab, which reads as "my document vanished". In VSTO and
      // dev (no native strip) this removes the current page from both the per-tab history
      // stack and recentLocations, then goes back; closeAll does it for every page.
      case 'tabs.close':
        if (hasNativeChromeTabs) pane.closeTab(pane.activeTabId.value)
        else pane.closeCurrentPage()
        return true
      case 'tabs.closeAll':
        if (hasNativeChromeTabs) pane.closeAllTabs()
        else pane.closeAllPages()
        return true
      // Only the demo-app host has a strip, so these exist only there (registry `available`).
      case 'tabs.new':
        pane.openNewTab()
        return true
      case 'tabs.list':
        toggleChromeTabList()
        return true
      case 'view.toolbar':
        if (isBookViewActive.value) bookViewStore.toggleToolbar(paneId)
        else if (isFastDocViewActive.value) sendFastDocViewCommand(paneId, 'toggleToolbar')
        else if (activeTab.value?.route === '/pdf-view') pane.togglePdfViewerTitleBar()
        return true
      case 'book.commentaryBottom':
        if (isBookViewActive.value) bookViewStore.toggleCommentaryPanel(paneId, 'bottom')
        return true
      case 'book.commentaryRight':
        if (isBookViewActive.value) bookViewStore.toggleCommentaryPanel(paneId, 'side')
        return true
      case 'book.commentaryLeft':
        if (isBookViewActive.value) bookViewStore.toggleCommentaryPanel(paneId, 'side-left')
        return true
      // The notes pane sits where the book view's bottom commentary does.
      case 'doc.notes':
        if (isFastDocViewActive.value) sendFastDocViewCommand(paneId, 'toggleNotes')
        return true
      case 'book.toc':
        if (isBookViewActive.value) bookViewStore.toggleTocPanel(paneId)
        return true
      case 'doc.toc':
        if (isFastDocViewActive.value) sendFastDocViewCommand(paneId, 'toggleToc')
        return true
      // Show all the current book's word-link marker signs, or hide them (all visible ->
      // hide all; otherwise show all). No-op on books without markers and on schema-v1 DBs.
      case 'book.markers':
        if (isBookViewActive.value && activeTab.value?.bookId != null)
          void settingsStore.toggleWordLinkMarkersForBook(activeTab.value.bookId)
        return true
      case 'book.search':
        if (isBookViewActive.value) bookViewStore.openSearch(paneId)
        return true
      case 'doc.search':
        if (isFastDocViewActive.value) sendFastDocViewCommand(paneId, 'openSearch')
        return true
      case 'nav.addressBar':
        options.toggleAddressBar()
        return true
      case 'nav.home':
        pane.goHome()
        return true
      case 'view.appBar':
        titleBarVisible.value = !titleBarVisible.value
        return true
      case 'view.theme':
        themeStore.toggleDarkMode()
        return true
      // The nav menu as a dropdown, and the same menu docked as this pane's rail - one family,
      // two presentations. Pane-scoped because each pane owns its own rail.
      case 'nav.mainMenu':
        options.toggleNavDropdown()
        return true
      case 'nav.sidebar':
        settingsStore.setNavSidebarVisible(paneId, !settingsStore.getNavSidebarVisible(paneId))
        return true
      default: {
        // The user's own shortcuts. "Open this book" goes through openBookTarget, the path a
        // book link takes: in place in the single-tab hosts (Word's pane), a new tab where
        // there is a strip.
        const own = isPersonalId(id) ? shortcuts.personalById(id) : undefined
        if (own?.kind === 'openBook') {
          const title = booksData.allBooksMap.get(own.bookId)?.title ?? own.title
          pane.openBookTarget({ route: '/book-view', bookId: own.bookId, title })
          return true
        }
        return false
      }
    }
  }

  /** Returns true when the event was consumed. */
  function handlePaneScoped(e: KeyboardEvent): boolean {
    // Ctrl+Tab / Ctrl+Shift+Tab - fixed (registry rows tabs.cycle / nav.historyBack).
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
    if (e.ctrlKey && !e.altKey && e.code === 'Tab') {
      if (e.repeat) return true
      if (hasNativeChromeTabs) toggleChromeTabList(e.shiftKey ? -1 : 1)
      else if (e.shiftKey) pane.goForward()
      else pane.goBack()
      return true
    }

    // The user's (or default) shortcuts. A combination can be bound to two ids at once only
    // when they live in pages never active together (book.toc and doc.toc), so running every
    // id this handler owns is safe.
    const ids = shortcuts.idsFor(e)
    if (ids.length > 0) {
      // Let a focused search input keep its own Ctrl+F (or whatever search is bound to).
      if ((ids.includes('book.search') || ids.includes('doc.search')) &&
          document.activeElement?.closest('[data-ctrlf-enabled]')) return false
      let consumed = false
      for (const id of ids) if (runPaneShortcut(id, e)) consumed = true
      if (consumed) return true
    }

    // Browser defaults that would hurt, swallowed whenever no shortcut claimed the key:
    // Ctrl+N opens a new browser window; Ctrl+T / Ctrl+Shift+T where there is no tab strip
    // would open a tab the user can never navigate back to.
    if (e.ctrlKey && !e.altKey && e.code === 'KeyN') return true
    if (e.ctrlKey && !e.altKey && e.code === 'KeyT' && !hasNativeChromeTabs) return true
    return false
  }

  /** Returns true when the event was consumed. Pane 1 only. */
  function handleAppWide(e: KeyboardEvent): boolean {
    for (const id of shortcuts.idsFor(e)) {
      switch (id) {
        case 'view.fullscreen':
          toggleFullscreen()
          return true
        // Hide scrollbars completely except while scrolling (persisted setting). App-wide
        // because the mode sits on the one shared root element.
        case 'view.scrollbars':
          toggleScrollbarsHidden()
          return true
        // Reading mode: hide/show all chrome at once (title bars, book toolbars, scrollbars).
        // Check-all semantics live in `toggleReadingMode`.
        case 'view.readingMode':
          toggleReadingMode()
          return true
        // Keep the screen on: off <-> the last mode chosen in settings.
        case 'view.keepScreenOn': {
          // Safety: always say which way it went, so it never stays on unnoticed.
          const on = settingsStore.toggleKeepScreenOn()
          showToast(t(on ? 'settings.keepScreenOn.toastOn' : 'settings.keepScreenOn.toastOff'))
          return true
        }
        case 'view.splitView':
          if (isSplitViewAvailable.value) bookViewStore.toggleSplitView()
          return true
      }
    }
    // Swallow the browser print dialog. This is preventDefault-only (no
    // stopPropagation), and `usePdfPrintShortcut` relies on that: on the PDF
    // page it still sees this keydown and forwards the print into the PDF.js
    // iframe.
    if (e.ctrlKey && !e.altKey && e.code === 'KeyP') return true
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
