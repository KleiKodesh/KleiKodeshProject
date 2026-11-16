/**
 * Every keyboard shortcut in the app, in one list: id, where it acts, its default
 * combination(s), its description, and whether the user may change it.
 *
 * The handlers (`useAppTitleBarShortcuts`, `useBookViewKeyboardShortcuts`, the documents
 * page) no longer compare key codes themselves for these: they ask the shortcuts store
 * which id a keydown is (`useShortcutsStore().idsFor(e)`) and run the action for that id.
 * The settings grid is drawn from this list, so the list and the behaviour cannot drift.
 *
 * `fixed` rows are listed for reference only. Their keys are either the browser's own
 * (F7 caret browsing), list navigation that must stay standard (arrows, Enter, Space,
 * F2/Delete in a list), the copy family, zoom (handled in four places with focus rules of
 * its own), or Ctrl+Tab, whose hold-to-switch is driven by the native tab strip. Their
 * handlers still match the keys directly. They take part in conflict checks, so no
 * changeable shortcut can be put on top of them.
 *
 * Order here is the order on screen.
 */
import { hasNativeChromeTabs, isVstoEnvironment } from '@/webview-host/bridge'
import type { Combo } from './shortcutCombo'

/**
 * Where a shortcut acts - used for conflict checks.
 *
 * `app` and `pane` act everywhere (app: once for the window, pane: in the focused pane),
 * so they clash with everything. The rest are pages or lists that are never focused at
 * the same moment, so one combination may mean different things in two of them (Ctrl+J
 * is the commentary panel in a book and the notes pane in a document).
 */
export type ShortcutScope = 'app' | 'pane' | 'bookView' | 'docView' | 'searchResults' | 'workspaceList'

export type ShortcutGroup = 'personal' | 'tabs' | 'navigation' | 'quickNav' | 'bookView' | 'documents' | 'searchResults' | 'view'

export interface ShortcutDef {
  id: string
  group: ShortcutGroup
  scope: ShortcutScope
  defaults: readonly Combo[]
  /** Key under `settings.shortcutsList`. */
  label: string
  /** Listed, never re-bindable. */
  fixed?: boolean
  /** False where the action does not exist in this host (no tab strip, no split view in
   *  Word's pane). Such a shortcut is neither listed, matched nor counted in conflicts.
   *  Constant per host, so it is a value, not a function. */
  available?: boolean
  /** Hidden from the list while this is false (pane too narrow), but still matched and
   *  still counted in conflict checks. */
  shown?: () => boolean
}

/** Group headings, in screen order, keyed to `settings.shortcutsList`. */
export const SHORTCUT_GROUPS: ReadonlyArray<{ id: ShortcutGroup; label: string; shown?: () => boolean }> = [
  // The whole group exists only where a native tab strip does: everywhere else the app
  // is single-tab, so every key in it would be a no-op.
  { id: 'tabs', label: 'tabManagement', shown: () => hasNativeChromeTabs },
  { id: 'navigation', label: 'navigation' },
  { id: 'quickNav', label: 'quickNavigation' },
  { id: 'bookView', label: 'bookView' },
  { id: 'documents', label: 'documents' },
  { id: 'searchResults', label: 'searchResults' },
  { id: 'view', label: 'view' },
]

// The side commentary columns need a wide enough pane (BookViewPage's 650px isWideScreen
// gate) - below it both keys return silently, so the rows are hidden. Measured on the window,
// which is the book pane outside split view and close enough inside it.
const SIDE_COMMENTARY_MIN_WIDTH = 650
const hasRoomForSideCommentary = () => typeof window === 'undefined' || window.innerWidth >= SIDE_COMMENTARY_MIN_WIDTH

const native = hasNativeChromeTabs

export const SHORTCUTS: readonly ShortcutDef[] = [
  // ── Tabs (native strip host only) ────────────────────────────────────────────────
  { id: 'tabs.new', group: 'tabs', scope: 'pane', defaults: ['Ctrl+KeyT'], label: 'newTab', available: native },
  { id: 'tabs.list', group: 'tabs', scope: 'pane', defaults: ['Ctrl+Shift+KeyT'], label: 'openTabList', available: native },
  { id: 'tabs.cycle', group: 'tabs', scope: 'pane', defaults: ['Ctrl+Tab'], label: 'holdCtrlAndCycleThroughTabs', fixed: true, available: native },
  { id: 'tabs.cycleBack', group: 'tabs', scope: 'pane', defaults: ['Ctrl+Shift+Tab'], label: 'cycleInReverse', fixed: true, available: native },
  // With a strip, Ctrl+W/X close real tabs and Alt+W/X remove pages from the tab's history.
  // Without one there is one tab by construction, so Ctrl+W/X and Alt+W/X all remove pages.
  { id: 'tabs.close', group: native ? 'tabs' : 'navigation', scope: 'pane', defaults: native ? ['Ctrl+KeyW'] : ['Ctrl+KeyW', 'Alt+KeyW'], label: native ? 'closeTab' : 'closeCurrentPage' },
  { id: 'tabs.closeAll', group: native ? 'tabs' : 'navigation', scope: 'pane', defaults: native ? ['Ctrl+KeyX'] : ['Ctrl+KeyX', 'Alt+KeyX'], label: native ? 'closeAllTabs' : 'closeAllPages' },
  { id: 'pages.close', group: 'tabs', scope: 'pane', defaults: ['Alt+KeyW'], label: 'closeCurrentPage', available: native },
  { id: 'pages.closeAll', group: 'tabs', scope: 'pane', defaults: ['Alt+KeyX'], label: 'closeAllPages', available: native },

  // ── Navigation ─────────────────────────────────────────────────────────────────
  { id: 'nav.addressBar', group: 'navigation', scope: 'pane', defaults: ['Ctrl+KeyE'], label: 'focusTheSearchBar' },
  // RTL: back is the RIGHT arrow, matching the title-bar button icons.
  { id: 'nav.back', group: 'navigation', scope: 'pane', defaults: ['Alt+ArrowRight'], label: 'goBackInHistory' },
  { id: 'nav.forward', group: 'navigation', scope: 'pane', defaults: ['Alt+ArrowLeft'], label: 'goForwardInHistory' },
  // Single-tab hosts: Ctrl+Tab walks the tab's own history (same handler as tabs.cycle).
  { id: 'nav.historyBack', group: 'navigation', scope: 'pane', defaults: ['Ctrl+Tab'], label: 'goBackInHistory', fixed: true, available: !native },
  { id: 'nav.historyForward', group: 'navigation', scope: 'pane', defaults: ['Ctrl+Shift+Tab'], label: 'goForwardInHistory', fixed: true, available: !native },
  { id: 'nav.home', group: 'navigation', scope: 'pane', defaults: ['Ctrl+KeyG'], label: 'goToHomePage' },
  { id: 'nav.mainMenu', group: 'navigation', scope: 'pane', defaults: ['Ctrl+KeyM'], label: 'openMainMenu' },
  { id: 'nav.sidebar', group: 'navigation', scope: 'pane', defaults: ['Ctrl+Shift+KeyM'], label: 'showHideSidebar' },
  { id: 'nav.workspaces', group: 'navigation', scope: 'pane', defaults: ['F8'], label: 'openWorkspaces' },
  { id: 'workspaces.rename', group: 'navigation', scope: 'workspaceList', defaults: ['F2'], label: 'renameWorkspaceInList', fixed: true },
  { id: 'workspaces.delete', group: 'navigation', scope: 'workspaceList', defaults: ['Delete'], label: 'deleteWorkspaceInList', fixed: true },
  { id: 'view.theme', group: 'navigation', scope: 'pane', defaults: ['Ctrl+KeyL'], label: 'toggleTheme' },

  // ── Quick navigation (each opens in a new tab, see useAppNavigation) ──────────────
  { id: 'go.catalog', group: 'quickNav', scope: 'pane', defaults: ['Ctrl+Digit1'], label: 'bookCatalog' },
  { id: 'go.search', group: 'quickNav', scope: 'pane', defaults: ['Ctrl+Digit2'], label: 'search' },
  { id: 'go.hebrewBooks', group: 'quickNav', scope: 'pane', defaults: ['Ctrl+Digit3'], label: 'hebrewbooks' },
  { id: 'go.openFile', group: 'quickNav', scope: 'pane', defaults: ['Ctrl+Digit4'], label: 'openFile' },
  { id: 'go.fileSearch', group: 'quickNav', scope: 'pane', defaults: ['Ctrl+Digit5'], label: 'fileSearch' },
  { id: 'go.dictionary', group: 'quickNav', scope: 'pane', defaults: ['Ctrl+Digit6'], label: 'dictionary' },
  { id: 'go.calendar', group: 'quickNav', scope: 'pane', defaults: ['Ctrl+Digit7'], label: 'calendar' },
  { id: 'go.measures', group: 'quickNav', scope: 'pane', defaults: ['Ctrl+Digit8'], label: 'measuresAndQuantities' },
  { id: 'go.hameshiv', group: 'quickNav', scope: 'pane', defaults: ['Ctrl+Digit9'], label: 'hameshiv' },
  { id: 'go.userSpace', group: 'quickNav', scope: 'pane', defaults: ['Ctrl+KeyU'], label: 'userSpace' },
  { id: 'go.settings', group: 'quickNav', scope: 'pane', defaults: ['F1'], label: 'settings' },

  // ── Book view ──────────────────────────────────────────────────────────────────
  { id: 'book.commentaryBottom', group: 'bookView', scope: 'bookView', defaults: ['Ctrl+KeyJ'], label: 'showHideCommentariesBelow' },
  { id: 'book.commentaryRight', group: 'bookView', scope: 'bookView', defaults: ['Ctrl+Shift+KeyJ'], label: 'showHideCommentariesOnTheRight', shown: hasRoomForSideCommentary },
  { id: 'book.commentaryLeft', group: 'bookView', scope: 'bookView', defaults: ['Ctrl+Alt+KeyJ'], label: 'showHideCommentariesOnTheLeft', shown: hasRoomForSideCommentary },
  { id: 'book.toc', group: 'bookView', scope: 'bookView', defaults: ['Ctrl+KeyK'], label: 'showHideTableOfContents' },
  { id: 'book.search', group: 'bookView', scope: 'bookView', defaults: ['Ctrl+KeyF'], label: 'search' },
  // RTL - Left = next.
  { id: 'book.nextSection', group: 'bookView', scope: 'bookView', defaults: ['Ctrl+ArrowLeft'], label: 'nextSection' },
  { id: 'book.previousSection', group: 'bookView', scope: 'bookView', defaults: ['Ctrl+ArrowRight'], label: 'previousSection' },
  { id: 'book.nextLine', group: 'bookView', scope: 'bookView', defaults: ['Ctrl+ArrowDown'], label: 'nextLine', fixed: true },
  { id: 'book.previousLine', group: 'bookView', scope: 'bookView', defaults: ['Ctrl+ArrowUp'], label: 'previousLine', fixed: true },
  { id: 'book.selectLine', group: 'bookView', scope: 'bookView', defaults: ['Enter'], label: 'selectLineAtCaret', fixed: true },
  { id: 'book.markers', group: 'bookView', scope: 'bookView', defaults: ['Ctrl+Shift+KeyU'], label: 'showHideCommentaryMarkers' },
  { id: 'book.cleanText', group: 'bookView', scope: 'bookView', defaults: ['Ctrl+KeyD'], label: 'removeRestoreVowelsAndCantillation' },
  { id: 'book.bookmark', group: 'bookView', scope: 'bookView', defaults: ['Ctrl+Shift+KeyD'], label: 'createBookmark' },

  // ── Documents (the FastDocView page: Word, RTF, text and web-archive files) ──────────
  { id: 'doc.notes', group: 'documents', scope: 'docView', defaults: ['Ctrl+KeyJ'], label: 'showHideNotesPane' },
  { id: 'doc.closeNotes', group: 'documents', scope: 'docView', defaults: ['Escape'], label: 'closeNotesPane', fixed: true },
  { id: 'doc.toc', group: 'documents', scope: 'docView', defaults: ['Ctrl+KeyK'], label: 'showHideTableOfContents' },
  { id: 'doc.search', group: 'documents', scope: 'docView', defaults: ['Ctrl+KeyF'], label: 'searchInDocument' },
  { id: 'doc.nextHeading', group: 'documents', scope: 'docView', defaults: ['Ctrl+ArrowLeft'], label: 'nextHeading' },
  { id: 'doc.previousHeading', group: 'documents', scope: 'docView', defaults: ['Ctrl+ArrowRight'], label: 'previousHeading' },
  { id: 'doc.copyIntoWord', group: 'documents', scope: 'docView', defaults: ['Ctrl+KeyV'], label: 'copyIntoWord', fixed: true },
  { id: 'doc.copyToSearch', group: 'documents', scope: 'docView', defaults: ['Ctrl+Shift+KeyC'], label: 'copyToLibrarySearch', fixed: true },
  { id: 'doc.cleanText', group: 'documents', scope: 'docView', defaults: ['Ctrl+KeyD'], label: 'removeRestoreVowelsAndCantillation' },

  // ── Full-text search results (list navigation, fixed) ─────────────────────────────
  { id: 'results.move', group: 'searchResults', scope: 'searchResults', defaults: ['ArrowUp', 'ArrowDown'], label: 'moveBetweenResults', fixed: true },
  { id: 'results.open', group: 'searchResults', scope: 'searchResults', defaults: ['Enter'], label: 'openResult', fixed: true },
  { id: 'results.openInNewTab', group: 'searchResults', scope: 'searchResults', defaults: ['Ctrl+Enter'], label: 'openResultInNewTab', fixed: true },
  { id: 'results.preview', group: 'searchResults', scope: 'searchResults', defaults: ['Space'], label: 'toggleResultPreview', fixed: true },

  // ── View ───────────────────────────────────────────────────────────────────────
  { id: 'view.splitView', group: 'view', scope: 'app', defaults: ['Ctrl+Backslash'], label: 'splitView', available: !isVstoEnvironment },
  // One toolbar toggle for every reader: book view, documents and PDF.
  { id: 'view.toolbar', group: 'view', scope: 'pane', defaults: ['Ctrl+KeyB'], label: 'showHideToolbar' },
  { id: 'view.zoomIn', group: 'view', scope: 'pane', defaults: ['Ctrl+Equal'], label: 'zoomIn', fixed: true },
  { id: 'view.zoomOut', group: 'view', scope: 'pane', defaults: ['Ctrl+Minus'], label: 'zoomOut', fixed: true },
  { id: 'view.zoomReset', group: 'view', scope: 'pane', defaults: ['Ctrl+Digit0'], label: 'resetZoom', fixed: true },
  { id: 'view.appBar', group: 'view', scope: 'pane', defaults: ['Ctrl+KeyH'], label: 'showHideAppBar' },
  { id: 'view.scrollbars', group: 'view', scope: 'app', defaults: ['Ctrl+Shift+KeyH'], label: 'showHideScrollbars' },
  { id: 'view.readingMode', group: 'view', scope: 'app', defaults: ['F9'], label: 'readingModeHideShowAllBarsAndScrollbars' },
  { id: 'view.keepScreenOn', group: 'view', scope: 'app', defaults: ['F4'], label: 'keepScreenOn' },
  { id: 'view.fullscreen', group: 'view', scope: 'app', defaults: ['F11', 'Ctrl+Shift+KeyF'], label: 'fullScreen' },
  // Chromium's own caret browsing - the app never sees an action for it.
  { id: 'view.caretBrowsing', group: 'view', scope: 'app', defaults: ['F7'], label: 'caretBrowsingForNavigationAndSelection', fixed: true },
]

export const SHORTCUTS_BY_ID: ReadonlyMap<string, ShortcutDef> = new Map(SHORTCUTS.map((d) => [d.id, d]))

/** True when a combination in scope `a` and the same combination in scope `b` would collide. */
export function scopesOverlap(a: ShortcutScope, b: ShortcutScope): boolean {
  return a === b || a === 'app' || a === 'pane' || b === 'app' || b === 'pane'
}
