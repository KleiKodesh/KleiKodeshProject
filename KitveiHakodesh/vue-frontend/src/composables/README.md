# src/composables

Shared composables used across multiple features. Feature-specific composables live inside their feature folder.

Only create a file here if the composable is used by two or more features. Single-feature logic stays in the feature folder.

That rule is enforced, not aspirational. `useTileGridKeys` (book-catalog only), `useTextSelectionKeys` and `useSelectAllInContainer` (book-view only) were moved out into their feature folders on 2026-07-29 for breaking it. If a second feature needs one of them, move it back here — that is the intended lifecycle, not a reason to leave it here pre-emptively.

**useAppNavigation.ts** — central navigation handler. Routes singletons via `navigateToSingleton`, handles the file picker, external links, and search navigation. Any code that needs to navigate between pages should use this, not call `tabStore` directly.

**useAppShellPane.ts** — `useAppShellPane(paneId)` wraps `tabStore` so every mutation routes to the correct split-view pane: `switchTab`, `closeTab`, `closeAllTabs`, `openTab`, `openNewTab`, `updateActiveTab`, `navigateToSingleton`, `goHome`, `togglePdfViewerTitleBar`. Shell chrome uses this — never call the `*Pane2*` tabStore functions directly.

**usePaneNavigation.ts** — `PANE_NAVIGATION_KEY` injection key plus `usePaneNavigation()`. Feature components inject this to get pane-scoped tab operations from the enclosing `AppShell`, so the same component works unchanged in either pane. Falls back to pane-1 behavior when used outside a shell.

**useVirtualScrollerKeys.ts** — `Ctrl+Home` / `Ctrl+End` keyboard navigation for `@tanstack/vue-virtual` scrollers. Must be wired up on every component that uses a virtualizer. The scroll container element must have `tabindex="0"`.

**useZoom.ts** — zoom via keyboard (`Ctrl+±/0`), wheel (`Ctrl+scroll`), and pinch. Range 50–200, step 10, default 100. `useZoomHandler` accepts a `zoom` ref, an optional `target` element, and an optional `enabled` flag — pass `enabled` to guard the handler so it only fires when the relevant page is active. Used in `BookViewPage`, `DictionaryPage`, and `SearchPage`.

**useListKeyNav.ts** — arrow-key, Home, and End navigation for plain DOM lists that own keyboard focus themselves (roving-focus model: the container has `tabindex="0"` and focus moves into it). Use only for standalone lists with no paired text input — TreeView, the title-bar nav dropdown, the catalog browse views.

**useInputListNavigation.ts** — the combobox keyboard model (W3C APG): DOM focus stays in a text input while its keydown events move a highlight through a paired list, so the user can keep typing at any moment. Handles ArrowUp/ArrowDown, PageUp/PageDown, Ctrl+Home/Ctrl+End, and Enter (Ctrl+Enter = new tab); plain Home/End and Left/Right stay caret keys. Works on plain containers (scrollIntoView over `[data-nav-item]`), `@tanstack/vue-virtual` lists (`getVirtualizer`), and tile grids (`getColumnsPerRow`). The returned `onKeydown` reports whether it consumed the event, so the input's own Enter/Escape handling can run otherwise; when input and list live in different components, the list component exposes the handler as `onSearchInputKeydown` and the input's owner forwards keydowns to it. Every input-paired list uses this — home search dropdown, address bar, book catalog search, HebrewBooks, local file search, the full-text-search filter panel.

**useLineCopy.ts** — intercepts the browser `copy` and `dragstart` events on a scroller element via `useScopedCopy()`. Calls the caller-supplied `buildFormattedHtml` to apply the active copy flags (`copyJoinLines`, `copySourcePosition`, `copyWithNotes`, `copyCleanText`), then writes `text/html` (RTL-wrapped) and `text/plain` (HTML-stripped) to the clipboard. When the user has selected all (`isSelectAll` ref is true), `buildFormattedHtml` grabs every line rather than only the DOM selection range.

**useDropdownClose.ts** — drop-in replacement for `onClickOutside` that also closes the dropdown when the browser window loses focus (e.g. clicking into a WebView iframe). Also solves the toggle-button race condition: pass `toggleButton` with the ref of the button that opens/closes the dropdown, and the composable will suppress the close handler when that button is clicked — preventing the sequence where `pointerdown` closes the dropdown and the subsequent `click` on the button reopens it. Returns `{ justClosed }` which the toggle handler can check as a fallback when the toggle button is in the same file. Use this on every dropdown instead of `onClickOutside` directly.

**useFloatingPanel.ts** — manages a floating panel positioned at fixed coordinates. Exposes position, visibility, and a computed style object for CSS left/top. Used for popup panels that are not draggable.

**useTabSwipeNavigation.ts** — swipe-to-navigate between tabs on touch devices and trackpads. Supports horizontal swipe gestures and two-finger trackpad swipes with configurable thresholds.

**useIframeScrollbarsAutoHide.ts** — keeps one iframe following the app-wide scrollbars mode (static / auto-hide) from `useUiChromeVisibility`. Same-origin frames (PDF.js viewer) get a style element plus a scroll-activity listener attached directly; cross-origin frames (local files in the WebView2 host) get an `htmlViewScrollbars` postMessage handled by the C#-injected IframeScrollScript in `JsBridge.cs`. The owning page must call the returned `apply` from its iframe load handler.

**useUiChromeVisibility.ts** — UI chrome visibility: per-pane session-only title bar (toggle via `Ctrl+H`) and the app-wide scrollbars mode — static or Windows-11-style auto-hide (transparent when idle, visible while scrolling). The mode is persisted in `settingsStore` (`app.scrollbarsAutoHide`, settings-page control included, toggle via `Ctrl+Shift+H`); this module owns the DOM effect — the `auto-hide-scrollbars` / `scrollbars-scrolling` classes on the root element (CSS in `main.css`) and the capture scroll listener that tracks activity. Also owns `toggleReadingMode` (F9) — a check-all/uncheck-all over title bars, book-view toolbars, and scrollbars auto-hide; reading mode is derived from the individual states, never stored, so the individual toggles keep working.
