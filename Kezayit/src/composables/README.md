# src/composables

Shared composables used across multiple features. Feature-specific composables live inside their feature folder under `src/components/`.

Only create a file here if the composable is used by two or more features. Single-feature logic stays in the feature folder.

**useAppNavigation.ts** — central navigation handler. Routes singletons via `navigateToSingleton`, handles the file picker, external links, and search navigation. Any code that needs to navigate between pages should use this, not call `tabStore` directly.

**useVirtualScrollerKeys.ts** — `Ctrl+Home` / `Ctrl+End` keyboard navigation for `@tanstack/vue-virtual` scrollers. Must be wired up on every component that uses a virtualizer. The scroll container element must have `tabindex="0"`.

**useZoom.ts** — zoom via keyboard (`Ctrl+±/0`), wheel (`Ctrl+scroll`), and pinch. Range 50–200, step 10, default 100.

**useListKeyNav.ts** — arrow-key, Home, and End navigation for plain DOM lists. Use this instead of hand-rolling keyboard handlers on any list.

**useTextSelectionKeys.ts** — `Ctrl+A` (select all) and `Ctrl+F` (open search) scoped to a specific element.

**useTileGridKeys.ts** — 2D arrow-key navigation for tile grids. Computes column count from container width to handle Up/Down correctly.

**useVirtualListKeyNav.ts** — arrow-key and `Ctrl+Home`/`Ctrl+End` for `@tanstack/vue-virtual` lists. Use this instead of `useVirtualScrollerKeys` when the list also needs arrow-key item navigation.

**useLineCopy.ts** — intercepts the browser `copy` event on a scroller. Writes lines as block elements in `text/html` and strips HTML for `text/plain` so copied text has no inline line breaks.

**useToolbarPosition.ts** — exports the `ToolbarPosition` type (`'top' | 'bottom' | 'left' | 'right'`). Import this type wherever toolbar position values are needed. The actual position state lives in `bookViewStore`.

**useDropdownClose.ts** — drop-in replacement for `onClickOutside` that also closes the dropdown when the browser window loses focus (e.g. clicking into a WebView iframe). Use this on every dropdown instead of `onClickOutside` directly. Accepts the same target and handler arguments, plus an optional `ignore` list.
