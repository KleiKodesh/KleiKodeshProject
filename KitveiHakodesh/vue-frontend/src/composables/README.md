# src/composables

Shared composables used across multiple features. Feature-specific composables live inside their feature folder.

Only create a file here if the composable is used by two or more features. Single-feature logic stays in the feature folder.

**useAppNavigation.ts** — central navigation handler. Routes singletons via `navigateToSingleton`, handles the file picker, external links, and search navigation. Any code that needs to navigate between pages should use this, not call `tabStore` directly.

**useAppShellPane.ts** — `useAppShellPane(paneId)` wraps `tabStore` so every mutation routes to the correct split-view pane: `switchTab`, `closeTab`, `closeAllTabs`, `openTab`, `openNewTab`, `updateActiveTab`, `navigateToSingleton`, `goHome`, `togglePdfViewerTitleBar`. Shell chrome uses this — never call the `*Pane2*` tabStore functions directly.

**usePaneNavigation.ts** — `PANE_NAVIGATION_KEY` injection key plus `usePaneNavigation()`. Feature components inject this to get pane-scoped tab operations from the enclosing `AppShell`, so the same component works unchanged in either pane. Falls back to pane-1 behavior when used outside a shell.

**useVirtualScrollerKeys.ts** — `Ctrl+Home` / `Ctrl+End` keyboard navigation for `@tanstack/vue-virtual` scrollers. Must be wired up on every component that uses a virtualizer. The scroll container element must have `tabindex="0"`.

**useZoom.ts** — zoom via keyboard (`Ctrl+±/0`), wheel (`Ctrl+scroll`), and pinch. Range 50–200, step 10, default 100. `useZoomHandler` accepts a `zoom` ref, an optional `target` element, and an optional `enabled` flag — pass `enabled` to guard the handler so it only fires when the relevant page is active. Used in `BookViewPage`, `DictionaryPage`, and `SearchPage`.

**useListKeyNav.ts** — arrow-key, Home, and End navigation for plain DOM lists. Use this instead of hand-rolling keyboard handlers on any list.

**useTextSelectionKeys.ts** — `Ctrl+A` (select all) and `Ctrl+F` (open search) scoped to a specific element.

**useTileGridKeys.ts** — 2D arrow-key navigation for tile grids. Computes column count from container width to handle Up/Down correctly.

**useVirtualListKeyNav.ts** — arrow-key and `Ctrl+Home`/`Ctrl+End` for `@tanstack/vue-virtual` lists. Use this instead of `useVirtualScrollerKeys` when the list also needs arrow-key item navigation.

**useLineCopy.ts** — intercepts the browser `copy` and `dragstart` events on a scroller element via `useScopedCopy()`. When the user has selected all (`isSelectAll` ref is true), copies every line as an HTML div wrapped in an RTL container; otherwise copies the user's text selection. Writes `text/html` and `text/plain` (HTML-stripped) so copied text has no inline line breaks.

**useDropdownClose.ts** — drop-in replacement for `onClickOutside` that also closes the dropdown when the browser window loses focus (e.g. clicking into a WebView iframe). Also solves the toggle-button race condition: pass `toggleButton` with the ref of the button that opens/closes the dropdown, and the composable will suppress the close handler when that button is clicked — preventing the sequence where `pointerdown` closes the dropdown and the subsequent `click` on the button reopens it. Returns `{ justClosed }` which the toggle handler can check as a fallback when the toggle button is in the same file. Use this on every dropdown instead of `onClickOutside` directly.

**useFloatingPanel.ts** — manages a floating panel positioned at fixed coordinates. Exposes position, visibility, and a computed style object for CSS left/top. Used for popup panels that are not draggable.

**useTabSwipeNavigation.ts** — swipe-to-navigate between tabs on touch devices and trackpads. Supports horizontal swipe gestures and two-finger trackpad swipes with configurable thresholds.

**useUiChromeVisibility.ts** — session-only UI chrome (title bar) visibility state. Toggle via `Ctrl+H`. The window listener is registered once at module load time so calling from multiple components never duplicates the handler.
