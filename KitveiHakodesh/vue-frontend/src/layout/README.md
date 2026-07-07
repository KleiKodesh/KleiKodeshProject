# layout

App shell components. Persistent chrome that wraps every page.

**AppTitleBar.vue** — 40px fixed header. Left side: hamburger menu, theme toggle, toolbar toggle, PDF filter toggle. Center: active tab title and TOC path (interactive breadcrumb for book-view tabs). Right side: home, new tab, close tab. Handles `Ctrl+W`, `Ctrl+X`, `Ctrl+J`, `Ctrl+F`. Add new global keyboard shortcuts here.

**AppPageView.vue** — fills remaining height, renders the active page by route. Book view and search are keyed by `activeTabId` and remount on tab switch. Adding a new route means registering it here.

**AppTitleBarTabDropdown.vue** — full tab list, opened by clicking the title bar center.

**AppTitleBarNavDropdown.vue** — hamburger nav menu, anchored to the right edge of its button so it opens toward the screen center.

**AppTitleBarTocBreadcrumb.vue** — renders the interactive TOC breadcrumb in the title bar center for `/book-view` tabs. Each segment label is shown as text; between non-last segments a chevron button opens `AppTitleBarBreadcrumbChevronDropdown` listing sibling entries. Receives `segments` from `useAppTitleBarTocBreadcrumb` and emits `navigate-to-entry`.

**AppTitleBarBreadcrumbChevronDropdown.vue** — teleported chevron dropdown that lists TOC sibling entries. Uses `useDropdownClose`, `<Teleport to="body">`, and `position: fixed` coordinates from `getBoundingClientRect`. Emits `select` with the chosen `TocEntry`.

**useAppTitleBarTocBreadcrumb.ts** — parses `tab.tocPath` (a `" / "`-separated string) into `TocBreadcrumbSegment[]`. Reads `TocEntry[]` from the `TocBridge` registered in `bookViewStore` — never queries the database directly. Returns an empty array for non-book-view tabs or when no bridge is registered yet.
