# layout

App shell components. Persistent chrome that wraps every page.

There is exactly one shell component; split view renders it twice. `src/App.vue` always renders pane 1 (`<AppShell :pane-id="1" />`) and renders pane 2 only while `bookViewStore.splitViewEnabled` is on, with a drag-handle divider between them. Split view requires a window at least `SPLIT_VIEW_MIN_WIDTH` (768px) wide and is disabled entirely in the VSTO/Word task-pane environment (`isVstoEnvironment`). Split/focus state (`splitViewEnabled`, `splitViewFraction`, `focusedPaneId`) lives in `bookViewStore`, not `tabStore`.

**AppShell.vue** — one shell instance = one pane. Takes `paneId?: 1 | 2` (default 1). Contains `<AppTitleBar :pane-id>` + `<AppPageView :pane-id>`. On setup as pane 2 it calls `tabStore.ensurePane2HasTab()` so pane 2 never renders empty. It `provide`s `paneId` and `PANE_NAVIGATION_KEY` so all descendants operate on the correct pane (see `usePaneNavigation` in `src/composables`). Any `pointerdown` inside the shell calls `bookViewStore.setFocusedPane(paneId)`.

**AppTitleBar.vue** — 40px fixed header, one per pane. Left side: hamburger menu, theme toggle, toolbar toggle, PDF filter toggle. Center: active tab title and TOC path (interactive breadcrumb for book-view tabs). Right side: home, new tab, close tab. Handles `Ctrl+W`, `Ctrl+X`, `Ctrl+J`, `Ctrl+F`. Add new global keyboard shortcuts here.

**AppPageView.vue** — fills remaining height, renders the active page via a route → async-component map. Picks the active tab from its `paneId` (`activeTabForPane(2)` vs `activeTab`). book-view, search, and txt-view are keyed by the active tab id and remount on tab switch. Adding a new route means registering it here.

**AddressBar.vue** — Explorer-style editable search field swapped into the title bar center on click (search mode). Reuses the home-page search engine (`useHomeSearch`) and dropdown (`HomeSearchDropdown`). The dropdown is open for the address bar's whole lifetime and doubles as the pane's tab list: it shows the open tabs — with the recently-opened documents (`recentlyOpenedStore`, the same collection as the home-page tiles) below them — whenever there are no search results (empty/short query, or a query that matched nothing) and the search results otherwise. Recent entries open in a new tab; search results navigate the current tab. There is no separate tab-list dropdown. Select and close only — there is no drag-reorder UI anywhere; tab order changes only via the MRU move-to-front in `tabStore`.

**AppTitleBarNavDropdown.vue** — hamburger nav menu, anchored to the right edge of its button so it opens toward the screen center.

**AppTitleBarTocBreadcrumb.vue** — renders the interactive TOC breadcrumb in the title bar center for `/book-view` tabs. Each segment label is shown as text; between non-last segments a chevron button opens `AppTitleBarBreadcrumbChevronDropdown` listing sibling entries. Receives `segments` from `useAppTitleBarTocBreadcrumb` and emits `navigate-to-entry`.

**AppTitleBarBreadcrumbChevronDropdown.vue** — teleported chevron dropdown that lists TOC sibling entries. Uses `useDropdownClose`, `<Teleport to="body">`, and `position: fixed` coordinates from `getBoundingClientRect`. Emits `select` with the chosen `TocEntry`.

**useAppTitleBarTocBreadcrumb.ts** — parses `tab.tocPath` (a `" · "`-separated string) into `TocBreadcrumbSegment[]`. Reads `TocEntry[]` from the `TocBridge` registered in `bookViewStore` — never queries the database directly. Returns an empty array for non-book-view tabs or when no bridge is registered yet.
