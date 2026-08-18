# layout

App shell components. Persistent chrome that wraps every page.

There is exactly one shell component; split view renders it twice. `src/App.vue` always renders pane 1 (`<AppShell :pane-id="1" />`) and renders pane 2 only while `bookViewStore.splitViewEnabled` is on, with a drag-handle divider between them. Split view requires a window at least `SPLIT_VIEW_MIN_WIDTH` (768px) wide and is disabled entirely in the VSTO/Word task-pane environment (`isVstoEnvironment`). Split/focus state (`splitViewEnabled`, `splitViewFraction`, `focusedPaneId`) lives in `bookViewStore`, not `tabStore`.

**AppShell.vue** — one shell instance = one pane. Takes `paneId?: 1 | 2` (default 1). Contains `<AppTitleBar :pane-id>` + `<AppPageView :pane-id>`. On setup as pane 2 it calls `tabStore.ensurePane2HasTab()` so pane 2 never renders empty. It `provide`s `paneId` and `PANE_NAVIGATION_KEY` so all descendants operate on the correct pane (see `usePaneNavigation` in `src/composables`). Any `pointerdown` inside the shell calls `bookViewStore.setFocusedPane(paneId)`.

**AppTitleBar.vue** — 40px fixed header, one per pane. Left side: hamburger menu, theme toggle, toolbar toggle, PDF filter toggle. Center: active tab title and TOC path (interactive breadcrumb for book-view tabs), replaced by `AddressBar.vue` in search mode. Right side: home, new tab, close tab. It owns the markup and the two pieces of local UI state the template needs (`searchMode`, `navDropdownOpen`) — keyboard shortcuts are **not** here, see below.

**useAppTitleBarShortcuts.ts** — every keyboard shortcut the title bar owns, installed once per pane. Add new shortcuts here, not in the component. Two groups, and picking the wrong one is the classic mistake: *pane-scoped* shortcuts (tab operations, book-view panels, navigation) fire only when that pane is focused, while *app-wide* ones (fullscreen, split view) are handled by pane 1 alone so they don't fire twice in split view. Also forwards `Ctrl`+key events out of child iframes (html/txt viewers) into the top-level keydown pipeline. Always match on `e.code`, never `e.key`. Update `ShortcutsReferenceList.vue` in the settings feature whenever a binding changes.

**AppPageView.vue** — fills remaining height, renders the active page via a route → async-component map. Picks the active tab from its `paneId` (`activeTabForPane(2)` vs `activeTab`). book-view, search, and txt-view are keyed by the active tab id and remount on tab switch. Adding a new route means registering it here.

**AddressBar.vue** — Explorer-style editable search field swapped into the title bar center on click (search mode). Reuses the global search engine and dropdown (`features/global-search/`: `useGlobalSearch`, `GlobalSearchDropdown`), the same pair behind the home page's search bar. The dropdown is open for the address bar's whole lifetime and lists `tabStore.recentLocations` — places the reader has been — whenever there are no search results (empty/short query, or a query that matched nothing), and the search results otherwise. It is **not** a tab list: nothing in it reflects which tabs are open, selecting a row navigates the current tab, and removing one closes nothing.

**Where the tab list lives.** Only the desktop host has one, and it is native: the FluentChromeTabs strip owns it, reached via `toggleChromeTabList()` in the bridge. `Ctrl+T` opens it; `Ctrl+Tab` / `Ctrl+Shift+Tab` open it as a hold-to-scroll switcher that stays up while `Ctrl` is held and activates the highlighted tab on release. That whole gesture is owned by C# (`TabListDropDown.BeginQuickSwitch`) — the popup takes OS focus as it opens, so the page never sees the matching keyup. Everywhere else (`!hasNativeChromeTabs`: VSTO task pane, dev browser) there is exactly **one tab** by construction — `tabStore.openTab` navigates in place instead of adding one, and `Ctrl+Tab` walks that tab's own back/forward history.

**AppTitleBarNavDropdown.vue** — hamburger nav menu, anchored to the right edge of its button so it opens toward the screen center.

**AppTitleBarTocBreadcrumb.vue** — renders the interactive TOC breadcrumb in the title bar center for `/book-view` tabs. Each segment label is shown as text; between non-last segments a chevron button opens `AppTitleBarBreadcrumbChevronDropdown` listing sibling entries. Receives `segments` from `useAppTitleBarTocBreadcrumb` and emits `navigate-to-entry`.

**AppTitleBarBreadcrumbChevronDropdown.vue** — teleported chevron dropdown that lists TOC sibling entries. Uses `useDropdownClose`, `<Teleport to="body">`, and `position: fixed` coordinates from `getBoundingClientRect`. Emits `select` with the chosen `TocEntry`.

**useAppTitleBarTocBreadcrumb.ts** — parses `tab.tocPath` (a `" · "`-separated string) into `TocBreadcrumbSegment[]`. Reads `TocEntry[]` from the `TocBridge` registered in `bookViewStore` — never queries the database directly. Returns an empty array for non-book-view tabs or when no bridge is registered yet.
