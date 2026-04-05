# layout

App shell components. Persistent chrome that wraps every page.

**AppTitleBar.vue** — 40px fixed header. Left side: hamburger menu, theme toggle, toolbar toggle, PDF filter toggle. Center: active tab title and TOC path. Right side: home, new tab, close tab. Handles `Ctrl+W`, `Ctrl+X`, `Ctrl+J`, `Ctrl+F`. Add new global keyboard shortcuts here.

**AppPageView.vue** — fills remaining height, renders the active page by route. Book view and search are keyed by `activeTabId` and remount on tab switch. Adding a new route means registering it here.

**AppTitleBarTabDropdown.vue** — full tab list, opened by clicking the title bar center.

**AppTitleBarNavDropdown.vue** — hamburger nav menu, anchored to the right edge of its button so it opens toward the screen center.
