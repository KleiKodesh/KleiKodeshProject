# App Specification

## Overview

- Hebrew book reader — mobile-first, tabbed navigation
- Features: browse books, full-text search, book viewer, file browser
- Strictly RTL, Hebrew-only: `dir="rtl"` and `lang="he"` on root HTML — do not change
- All user-facing text must be in Hebrew — no English strings in templates, error messages, loading states, placeholders, tooltips, or any visible UI text

## Folder Structure

- `src/features/` — one folder per app feature (e.g. `book-catalog/`, `book-view/`, `full-text-search/`)
- Most feature folders keep files flat with no nested subfolders
- Exceptions: `book-view/` has three subfolders (`lines/`, `toc/`, `commentary/`) due to complexity, and `halachic-units/` has `units/` for per-system unit definitions
- Sub-components named after parent: `BookCard.vue` → `BookCardCover.vue`, `BookCardMeta.vue`
- Shared reusable components (used across features): `src/components/`, with `src/components/common/` for shared search chrome
- App shell components (split container, pane shell, title bar, page router): `src/layout/` — plus `App.vue` at the root, which owns split view
- Shared composables across features: `src/composables/`
- Shared pure utils: `src/utils/`
- Pinia stores: `src/stores/` — never under `src/data/` or elsewhere
- Host/data access layer: `src/webview-host/`
- Theme system: `src/theme/`; global stylesheet: `src/assets/styles/main.css`
- `src/stubs/` — build-time module stubs (e.g. the temporal polyfill stub)

## RTL Layout

This app is strictly RTL. Every spatial decision must be understood in physical screen terms:

| Concept               | Physical screen position |
| --------------------- | ------------------------ |
| inline-start          | RIGHT side of screen     |
| inline-end            | LEFT side of screen      |
| Reading direction     | Right → Left             |
| First item in a row   | Appears on the RIGHT     |
| Tree/list indentation | Shifts toward the LEFT   |

- "Right" always means the physical right side of the screen — no ambiguity, no exceptions
- "Left" always means the physical left side of the screen — no ambiguity, no exceptions
- `inline-start` = physical RIGHT. `inline-end` = physical LEFT.
- **CRITICAL — border/padding/margin direction cheatsheet in RTL:**
  - `border-inline-start` = border on the physical RIGHT edge
  - `border-inline-end` = border on the physical LEFT edge
  - `padding-inline-start` = padding on the physical RIGHT side
  - `padding-inline-end` = padding on the physical LEFT side
  - `margin-inline-start` = margin on the physical RIGHT side
  - `margin-inline-end` = margin on the physical LEFT side
  - In a flex row, `flex-start` = physical RIGHT, `flex-end` = physical LEFT
  - In a flex row, the **first child** renders on the physical RIGHT
  - `align-items: flex-start` on a column = aligns to physical RIGHT
  - `align-items: flex-end` on a column = aligns to physical LEFT
- Use logical properties (`padding-inline-start`, `margin-inline-end`, etc.) for flow content
- Use physical `left`/`right` for `position: absolute/fixed` overlays and panels — not `inset-inline-start/end`
- Side panel on the right: `position: absolute; right: 0`
- Side panel on the left: `position: absolute; left: 0`
- Slide in from right: start at `translateX(100%)`, animate to `translateX(0)`
- Slide in from left: start at `translateX(-100%)`, animate to `translateX(0)`
- Tree chevrons: collapsed → `IconChevronLeft` (points left toward children); expanded → `IconChevronDown`
- `IconTextBulletListTree` must always have `class="rtl-flip"` (`transform: scaleX(-1)`) — it's an LTR-designed icon

## Navigation

- Page-navigation app — navigating replaces the current tab's content in-place
- **The app is split-view capable: there are up to two panes, each with its own tab list and active tab.** Every navigation must target a specific pane.
- From inside a pane's component tree, inject `PANE_NAVIGATION_KEY` (from `@/composables/usePaneNavigation`) and call `updateActiveTab` / `openTab` / `openOrUpdateActiveTab` / `navigateToSingleton` / `switchTab` on it. Calling `useTabStore().updateActiveTab(...)` directly always targets pane 1 and will navigate the wrong pane when split view is on.
- Components that need to know which pane they are in inject `'paneId'`.
- `useAppShellPane(paneId)` is the composable behind the provided API — use it when you need pane-scoped operations outside the injection context.
- Store-level pane-2 equivalents exist (`pane2ActiveTabId`, `openPane2Tab`, `updatePane2ActiveTab`) but are implementation details of `useAppShellPane`; prefer the injected API.
- `openTab` is reserved for explicitly creating a second tab (e.g. a "new tab" button)
- Keying in `AppPageView`: `/book-view` is keyed by tab id + book id, `/search` and `/txt-view` by tab id — these fully remount on tab switch, so setup-time reads of the active tab are stable for the component's lifetime. All other routes share one instance.

### Singleton pages

Singleton tabs allow only one tab of each route at a time. The authoritative list is `SINGLETON_ROUTES` in `tabStore.ts`: `/settings`, `/books`, `/hebrewbooks`, `/workspaces`, `/hebrew-calendar`, `/dictionary`, `/midot`, `/file-search`. Always navigate to them via `navigateToSingleton(route)` — the pane-scoped one from the injected API — never `updateActiveTab` or `openTab` directly. If a tab with that route already exists it switches to it; otherwise the current tab is replaced in-place. Hebrew titles come from `SINGLETON_TITLES` in the same file, so a new singleton route needs an entry in both.

These routes are also never persisted across sessions — `persistTabs` strips them before writing.

## Design Language

The visual style is a deliberate blend of two design systems:

- VSCode provides the structural foundation: color palette, flat chrome, thin borders, muted text hierarchy, and overall density
- Windows 11 Fluent provides the interaction feel: `4px` rounded corners on controls, subtle depth via `color-mix()` tints, smooth motion, touch-friendly sizing, and Fluent icons throughout

Neither system dominates — VSCode sets the colors and layout, Fluent sets the shape language and tactile quality. The result is clean and editor-like, but warm and touch-friendly rather than purely utilitarian.

- Targets small Android-type screens with touch — minimum 44px touch targets
- Compact sizing: title bar 40px, book-view toolbar 32px, list rows 44px, breadcrumb 32px, home tiles 48px icon size
- Default theme: `vscode-dark` — use `vscode-dark` / `vscode-light` as the reference for all color decisions
- VSCode dark palette: bg `#1e1e1e`, sidebar `#252526`, toolbar `#2d2d2d`, border `#3c3c3c`, text `#d4d4d4`, secondary `#858585`, accent `#0078d4`
- VSCode light palette: bg `#ffffff`, sidebar `#f3f3f3`, toolbar `#ebebeb`, border `#e7e7e7`, text `#616161`, accent `#007acc`
- Font: `Segoe UI Variable` → `Segoe UI` → `system-ui` — the native Windows 11 typeface

### Buttons & Motion

- Global button `border-radius: 4px` — Fluent feel, defined in `main.css`, applies everywhere
- Global active shrink: `button:active { transform: scale(0.92) }` defined in `main.css`
- Global button defaults (hover bg, text color, active shrink) live in `main.css` — do not repeat `background`, `border`, `cursor`, `transition`, `color`, `:hover`, or `:active` in component styles; only add layout props (`width`, `height`, `padding`, `display`) and `.active` color locally
- Motion: scale transitions on tiles 150ms; background/color transitions 100–150ms

### Home Tiles

- Container: solid `var(--bg-secondary)`, `border-radius: 12px`, filled icon with explicit color, label below 11px
- Tile colors: ספרים `#C1440E`, חיפוש `#3478f6`, פתח קובץ `#f0a500`, הגדרות uses `IconSettings24` from `vue-fluent-color`
- Hover: `transform: scale(1.08)` on icon container, active `scale(0.95)` — no background color change on tile

### List Rows (FS Browser)

- No icon container — plain filled icon inline, always colored (folders `#f0a500`, books `#C1440E`)
- No color change on hover — background highlight only on hover/active

### Flat List & Tree Design

- All flat lists and treeviews use flat design — no rounded corners on rows, no card treatment
- Hover/active state: full-width background tint only — `color-mix(in srgb, var(--text-primary) 6%, transparent)` hover, `10%` active
- Row height 32px for dense trees, 44px for touch-primary lists
- Chevrons use `var(--text-secondary)` color, same row height as the row itself
- No `border-radius` on individual rows — the list container may have rounded corners if needed

### Search Inputs

- All search input containers use `border-radius: 999px` — Windows 11 pill style, no exceptions
- Container background: `var(--input-bg)` with `border: 1px solid var(--border-color)`
- Icon inline inside the container, no outer wrapper border
- Never use `border-radius: 0` or flat/rectangular corners on any search input container
- Search inputs never change background color on focus — the visual background lives on the container, not the input itself; `input:focus { background: none !important }` is set globally in `main.css`
- Clear button desaturated: `.search-input::-webkit-search-cancel-button { filter: grayscale(1) opacity(0.4) }`

### Book View — Commentary & TOC Compact Sizing

These components live inside the split-pane bottom panel and TOC side panel, where vertical space is at a premium. They intentionally use tighter sizing than the rest of the app:

| Element                                  | Size                            | Notes                                         |
| ---------------------------------------- | ------------------------------- | --------------------------------------------- |
| `CommentaryHeader` row height            | 32px                            | vs 40px title bar                             |
| `CommentaryHeader` buttons               | 24×24px, 14px icons             | vs 32×32px elsewhere                          |
| `CommentaryHeader` title font            | 13px                            | vs 14px standard                              |
| `CommentaryHeaderNav` row height         | 32px                            | matches header                                |
| `CommentaryHeaderNav` buttons            | 24×24px, 14px icons             | same as header                                |
| `CommentaryHeaderNav` search input       | 20px tall, 11px font            | compact pill                                  |
| TOC search input                         | 12px font, `padding: 4px 8px`   | bottom of TOC panel                           |
| `CommentaryTreeViewNode` book row        | `height: 28px` fixed            | compact secondary nav                         |
| `CommentaryTreeViewNode` section header  | `height: 28px` fixed            | same height as book rows                      |
| `CommentaryTreeViewNode` expander button | `width: 24px`, `height: 100%`   | no bg, no border, no active effect            |
| `CommentaryTreeViewNode` label           | `white-space: nowrap`, ellipsis | never wrap — wrapping breaks fixed row height |

Both `CommentaryHeader` and `CommentaryHeaderNav` use `background: var(--bg-primary)` — same as the commentary view content — so they blend in rather than standing out as a distinct toolbar.

### Misc UI

- Use `color-mix()` tinted backgrounds instead of solid fills for icon containers in list contexts
- Rounded corners: `4px` small controls, `8px` cards, `999px` search/input pills, `12px` tile icon containers
- Secondary toolbars use `var(--bg-toolbar)` — between `--bg-primary` and `--bg-secondary`
There are two independent resize dividers — do not confuse them:

- **Book view bottom-panel divider** (`SplitPane.vue`, horizontal): hover `color-mix(in srgb, var(--text-secondary) 25%, transparent)` — never accent color. Visual height 1px, touch target 20px via a `::before` pseudo-element (`position: absolute; height: 20px; top: 50%; transform: translateY(-50%)`).
- **Split-view pane divider** (`App.vue`, vertical, between the two `AppShell` panes): 4px wide, `background: var(--border-color)`, hover `color-mix(in srgb, var(--accent-color) 50%, transparent)`, with a 20px-wide `::before` touch target. This one *does* use the accent color on hover.

Always keep the enlarged-pseudo-element touch-target pattern on any resize handle.

## Dropdowns

Dropdown anchor depends on which physical side of the screen the toggle button sits on — there is no single universal rule:

| Toggle button location                    | Correct anchor | Reason                                                  |
| ----------------------------------------- | -------------- | ------------------------------------------------------- |
| Physical right side (inline-start in RTL) | `left: 0`      | Dropdown opens leftward, stays on screen                |
| Physical left side (inline-end in RTL)    | `right: 0`     | Dropdown opens rightward toward center, stays on screen |

- `AppTitleBarNavDropdown` — hamburger is on the physical left → `right: 0`
- Most other dropdowns (commentary type, filter panels) — toggle is on the physical right → `left: 0`
- Always ask: "will this dropdown go off-screen?" and anchor toward the center
- Extract every dropdown to its own component — never inline dropdown markup in a parent component
- All dropdown lists that scroll must use `scrollbar-width: thin; scrollbar-color: var(--border-color) transparent` — never the default fat scrollbar

## Vue Patterns

- Prefer `nextTick` over `setTimeout` when waiting for the DOM to update after a reactive change — `nextTick` is deterministic and tied to Vue's render cycle
- Example: focusing an input after a panel opens → `onMounted(() => nextTick(() => inputRef.value?.focus()))`

## Virtual Scroller Keyboard Navigation

Every component that uses `useVirtualizer` from `@tanstack/vue-virtual` must wire up `useVirtualScrollerKeys` from `@/composables/useVirtualScrollerKeys`, passing the scroller element ref, a virtualizer getter, and an item count getter.

- The scroll container element must have `tabindex="0"` so it can receive keyboard focus
- Ctrl+Home scrolls to the first item then sets `scrollTop = 0`
- Ctrl+End scrolls to the last item then sets `scrollTop = scrollHeight`
- The composable uses `useEventListener` from VueUse and cleans up automatically

## Virtual Scroller — Programmatic Scroll to a Specific DOM Element Within an Item

When you need to scroll not just to a virtualizer item but to a specific DOM element *within* that item (e.g. a `<mark>` inside a rendered line), there are two distinct cases with different solutions. The canonical implementation is in `scrollToIndexWithRetry.ts` and `BookViewLinesContent.vue`.

### Item already in the measurements cache (already rendered)

Never call `virtualizer.scrollToIndex()` when the item is already measured. That call is asynchronous — it runs in a later frame and will overwrite any `scrollTop` you set, snapping the scroll position back to where the virtualizer wants it.

Instead: set `scrollTop` directly to place the item at the correct position, then use a `MutationObserver` on the scroller element to detect when the target DOM element actually appears or gets its final CSS class, and adjust `scrollTop` from `getBoundingClientRect()` at that point. Two `requestAnimationFrame` calls before starting the observer covers the fast path (DOM already ready); the observer handles the slow path where a render cache invalidation means the item HTML hasn't been repainted yet.

Always set a timeout (500ms is safe) to disconnect the observer in case the element never appears.

### Item not yet in the measurements cache (outside rendered range)

Call `virtualizer.scrollToIndex({ align: 'start' })` to bring the item into the rendered range, then retry in subsequent `requestAnimationFrame` calls until the item appears in `measurementsCache`. Once it does, switch to the direct `scrollTop` strategy above — do not keep calling `scrollToIndex` after the item is measured or it will fight your `scrollTop` assignments.

### Key rules

- `virtualizer.scrollToIndex()` and direct `scrollTop` assignment are mutually exclusive — never mix them for the same scroll operation.
- `getBoundingClientRect()` returns stale values until after the browser has painted. Always read it inside a `requestAnimationFrame` (or the MutationObserver callback, which fires after the DOM mutation but before the next paint — one rAF after the callback gives accurate layout values).
- A render cache keyed on reactive props (like `currentMatchOccurrence`) means the item's HTML is re-rendered asynchronously after the prop changes. `nextTick` alone is not enough — the virtualizer has its own render cycle on top of Vue's. The MutationObserver approach is the only reliable way to detect when the repainted DOM is actually ready.

## Database

- All seforim SQLite access goes through `src/webview-host/seforimDb.ts` — never call fetch against the DB from a component or composable. (There is no `db.ts`.)
- All raw SQL strings for the seforim DB live in `src/webview-host/queries.sql.ts` — no inline SQL anywhere else
- Feature composables call `query()` with a SQL constant from `queries.sql.ts` and a params array
- `seforimApi.ts` is the typed request layer above `seforimDb.ts`; `serviceClient.ts` speaks to the native KitveiHakodesh service. Prefer `seforimApi.ts` for new data access over hand-writing a `query()` call.
- Each additional database owns its own SQL file next to its access module: `dictionaryDb.ts` + `dictionaryDb.sql.ts`, `userSettingsDb.ts` + `userSettingsDb.sql.ts`. `dictionarySeforimDb.ts` holds seforim-specific dictionary queries.
- **Dictionary and user-settings DBs**: their SQL lives beside their own access module, not in `queries.sql.ts`. Both the C# host path and the dev path execute the same SQL string sent from the frontend — there is nothing to keep in sync between C# and dev for these.

### Transports (auto-selected at runtime)

1. C# WebView host — when `window.__webviewQuery` is injected by the host before the app boots
2. The native KitveiHakodesh service — used by browser dev mode, reached over the private loopback endpoint via `serviceClient.ts`

Browser dev mode is **fully service-dependent**: there is no JavaScript SQLite shim and no `npm run dev:server` script any more. `VITE_DB_URL` in `.env.development` still exists as an override. Because the service backs dev mode, `isHosted` is `true` in dev — see the runtime-context rule in `preferences.md`.

### Schema Reference

The seforim schema lives in **`database-schema.md`** — it is the single source of truth. It used to be duplicated here; that copy was removed so the two cannot drift apart. Do not re-add schema tables to this file.

## Persistence

Persistence uses six IndexedDB databases plus localStorage — all access goes through `src/utils/persistence.ts`. The authoritative list of databases is the `handles` map in that file; the docblock at the top of the file has drifted from it, so trust `handles`.

### Why localStorage exists here

Opening an IndexedDB database has a measurable async cost (~65ms cold on WebView2) even for a single key read. For the app-reset flag — a boolean checked on every boot but set only when the user explicitly resets the app — this cost is unacceptable. The flag is stored in `localStorage` under key `__pendingReset` so the boot check is synchronous and zero-cost on normal launches. This is the only legitimate use of localStorage in the app; all other persistence goes through IDB.

### IDB performance rule

Every IDB open has a real async cost, especially cold on WebView2. Before adding any new boot-time IDB read, ask whether it can be deferred until after mount, batched into an existing transaction, or replaced with a synchronous localStorage read. Never add a new `await idbGet()` call to the startup sequence without justification.

For runtime hot paths (opening a book, switching tabs, every search), prefer an in-memory cache in front of IDB. The pattern is: check the cache first, read IDB on miss, populate the cache, return. Writes update the cache immediately and write to IDB fire-and-forget or via the existing pending-save promise chain.

### In-memory cache rules

When adding an in-memory cache in front of IDB, always answer these questions before shipping:

- What is the maximum number of entries this cache can hold? If unbounded, add an explicit cap.
- When are entries evicted? Every cache tied to a lifecycle (tab, session, workspace) must evict when that lifecycle ends — e.g. the book-state cache is evicted when its tab closes. Caches not tied to a lifecycle must have a size cap with FIFO or LRU eviction.
- Does the cache stay consistent with IDB writes? Every write path must update the cache before or alongside the IDB write — never write to IDB without also updating the cache.
- Is the cached value large? Never cache result sets, arrays of objects, or anything that scales with user data — only cache scalars, small structs, and key lists. Large data belongs in IDB only.

Current caches and their caps:

- `bookStateCache` in `stores/tabStatePersistence.ts` — one entry per open tab×book; evicted by `deleteAllStateForTab`, which every close path calls
- `lastReadCache` in `stores/bookLastRead.ts` — capped at 200 entries, FIFO eviction (the *on-disk* cap of 1000 is separate; both live in that same module)
- `_mem` in `searchCacheStore` — **not cached in memory** — search results can be hundreds of items with snippet strings; only the LRU key list (`_lru`) is kept in memory

### localStorage rules

- All localStorage access must go through `src/utils/persistence.ts` — never call `localStorage` directly anywhere else in the codebase. The one deliberate exception is the unprefixed `__pendingReset` flag, which `appResetState.ts` reads and writes through the driver's `lsGetRaw`/`lsSetRaw`/`lsDeleteRaw` primitives.
- **Every localStorage key is namespaced `area.name`** — `text.fontSize`, `search.expandKetiv`, `bookView.toolbarVisible`. Never add a bare key. localStorage is one flat namespace, and two owners claiming the same bare name overwrite each other silently, with no error: the setting simply resets on every launch.
- **Define a new key in the module that owns the value, not in a central registry.** There is no `KEYS` file — `settingsStore` defines its own settings keys, `bookViewStore` its UI-state keys, `themeStore` its one key, and a feature that owns a preference defines it locally (exporting it only if a second module genuinely reads the same value, as `hebrew-calendar/useZmanim.ts` does with `ZMANIM_CITY_KEY`).
- Structural (non-setting) keys use the `tabs:` and `workspaces.` prefixes. That is how `clearPersistedSettings` in `settingsStore` tells structure from preferences — it preserves by prefix rather than by a hardcoded list of names, so a new structural key is covered automatically as long as it lands under one of those namespaces.
- Full app reset must leave no state behind: it drops every database, then calls `lsClearAll()` (which clears the whole `kitvei-hakodesh.` namespace), then removes `__pendingReset` explicitly, last.

### IDB databases

| Database                | Contents                                                   |
| ----------------------- | ---------------------------------------------------------- |
| `app-tabs`              | Tabs list, tab states, book states (workspace-scoped keys) |
| `app-lastread`          | Per-book last-read positions (LRU-capped at 1000)          |
| `app-search-cache`      | FTS search result cache (LRU-capped at 100 queries)        |
| `app-dict-cache`        | Dictionary lookup cache                                    |
| `app-catalog-toc-cache` | Book catalog TOC search result cache (LRU-capped at 25)    |
| `app-recently-opened`   | Recently opened documents (LRU-capped at 16)               |

All scalar settings (fonts, zoom, theme, toolbar state, workspaces, calendar prefs, etc.) live in localStorage via `lsGet`/`lsSet` — synchronous, zero async cost, auto-prefixed with `kitvei-hakodesh.`. IDB is only used for data that is too large or structured for localStorage.

Settings that must be shared with the native host live in the user-settings database instead, accessed via `src/webview-host/userSettingsDb.ts` — not through `persistence.ts`. Use this only when the C# side needs to read the value too; browser-only preferences stay in localStorage.

All browser persistence access must go through `src/utils/persistence.ts` — no component, composable, or store may call `localStorage` or a raw IDB API directly.

One structural exception, which is a property of the driver rather than a loophole: each database the driver manages is a flat key→blob bucket (a single `data` object store, out-of-line keys, no indexes). A caller whose schema needs in-line keys (`keyPath`), a secondary index, or multiple object stores therefore *cannot* use the driver and must hold its own handle. `hebrewBooksHistoryStore` is the one qualifying case (`keyPath: 'id'` plus a `lastAccessed` index). Any such owner must export a `drop*` function for `appResetState` to call, because `deleteDatabase` stalls silently on `onblocked` while a handle is still open. Needing a *different key name* is never a reason to open your own database.

Components and composables should reach persistence through a store (`tabStore`, `bookViewStore`, `settingsStore`) rather than importing `persistence.ts` directly. The exception is a self-contained cache module that owns its own IDB database and is not shared state — `dictionaryCache.ts` and `bookCatalogTocSearchCache.ts` import the `idb*` helpers directly by design, matching the "single feature → plain module, no Pinia" rule in `preferences.md`. A component or composable importing `persistence.ts` to read or write *app state* is still wrong; that belongs in a store.

Known violations of the spirit of this rule that should move behind a store when next touched: `FullTextSearchPage.vue`, `HebrewCalendarPage.vue`, `useZmanim.ts`, `useNextZman.ts`, and `useAutofill.ts` all import `persistence.ts` directly.

### Key scheme

localStorage keys are prefixed with `kitvei-hakodesh.` automatically by `lsGet`/`lsSet`, so every name a caller passes is the unprefixed `area.name` form. Each owning module declares its own; `persistence.ts` holds no key names at all.

`app-tabs` keys are workspace-scoped:

| Key                            | Value              |
| ------------------------------ | ------------------ |
| `tabs:{wsId}`                  | `PersistedTabList` |
| `tab:{wsId}:{tabId}`           | `TabState`         |
| `book:{wsId}:{tabId}:{bookId}` | `BookState`        |

`app-lastread` keys: `lastread:{bookId}` → `LastReadState`

### Stores

- `tabStore` — tab lifecycle, navigation, tab/book state, and lastread
- `bookViewStore` — toolbar/searchBarPos; reads from localStorage at init (synchronous)
- `settingsStore` — all app settings in localStorage; `init()` is synchronous
- `themeStore` — theme preset + reading background in localStorage
- `workspaceStore` — workspace list in localStorage; `init()` is synchronous

### lastread LRU cap

Always use `tabStore.setLastReadPos()` — it goes through `bookLastRead.ts`, which enforces both the 200-entry memory cap and the 1000-entry on-disk cap.

### App reset

`resetEverything()` in `src/features/settings/appResetState.ts` owns the full reset. It lives there — not in a store — because it spans every domain: it wipes all seven IDB databases plus all of localStorage, so no single store owns it. That module also owns the `resetting` flag `App.vue` reads to show the blocking overlay, and `resetEverything` sets the flag itself, so callers don't have to.

The sequence is: set `resetting` → `scheduleReset()` → the wipe → `resetHostApp()` (which resets the FTS index and C# settings, then reloads). All of it lives in `appResetState.ts`; none of it is in the storage driver.

`scheduleReset()` writes the `__pendingReset` localStorage key. That is a **crash-safety net, not the reset mechanism** — the wipe itself is eager. On next boot `checkAndExecPendingReset()` (called from `main.ts`) checks the flag synchronously; if it is still set the previous reset died partway through, so it redoes the wipe and reloads. The normal path clears the flag at the end of the wipe, so the boot check returns immediately.

The reset is a **sequencer, not an operation** — it spans four subsystems (IndexedDB, localStorage, the C# host, the page lifecycle) and the driver owns only one of them. That is why it must not live in `persistence.ts`: a sequencer has to reach every participant, so putting it at the bottom of the stack forces the bottom to import from the top. It previously did exactly that, via `await import('@/stores/...')`.

Two ordering rules keep that net armed. Both are load-bearing — do not "tidy" either one:

- The wipe clears localStorage **last**, after every database is dropped: `lsClearAll()` then `lsDeleteRaw(RESET_LS_KEY)`. Removing the flag earlier would mean a mid-wipe crash leaves no flag and the next boot never retries. (`lsClearAll` only clears the `kitvei-hakodesh.` namespace — the flag is unprefixed, which is why it takes its own explicit delete. Dying between those two calls is safe: the flag survives and the next boot redoes an already-finished wipe, which is a no-op.)
- `checkAndExecPendingReset()` does **not** clear the flag before its recovery wipe, for the same reason. It only drops the flag if the wipe itself throws — a persistently failing wipe must not break startup on every launch, so booting with stale data wins over not booting.

### Adding new persisted state

Add the key or field to the module that **owns** the value. Never to `persistence.ts` — it is a driver and holds no schemas or key names.

- Global scalar setting → add a namespaced key to the `KEYS` object in `settingsStore.ts` (plus a `DEFAULTS` entry, a ref, `loadSetting`, `persistSetting`), and export the ref
- Per-tab UI state → add a field to `TabState` in `stores/tabStatePersistence.ts`, expose via `tabStore.getTabViewState/setTabViewState`
- Per-tab+book state → add a field to `BookState` in `stores/tabStatePersistence.ts`, expose via `tabStore.getBookViewState/setBookViewState`
- Per-book global state → add a field to `LastReadState` in `stores/bookLastRead.ts`, expose via `tabStore.getLastReadPos/setLastReadPos`
- Book-view UI state → add a namespaced key to the `KEYS` object in `bookViewStore.ts`
- Boot-time flag that must be synchronous → use localStorage via `persistence.ts`, declare the constant in the module that owns the flag, and ensure the wipe in `appResetState.ts` removes it

## HebrewBooks Downloads

- HebrewBooks blocks direct HTTP downloads — all downloads must go through the WebView2 browser engine
- Never use `HttpClient` or any direct HTTP fetch to download HebrewBooks PDFs
- The download URL format is: `https://download.hebrewbooks.org/downloadhandler.ashx?req={bookId}`
- C# intercepts the browser download via `DownloadStarting` event and redirects the file path
- Open-in-viewer: redirect to cache folder, suppress dialog, push `hbPdfReady` event when complete
- Save As: show native `SaveFileDialog` to let user pick destination, browser handles the actual download

## C# Backend

The backend is **not** uniformly net48 — the language rules depend on which project you are editing. Check the `.csproj` before writing code.

| Project | Target | Language rules |
| --- | --- | --- |
| `KitveiHakodeshLib`, `KitveiHakodeshDemoApp` | net48 | C# 7.3 — the restrictions below apply |
| `DocumentLocator.Client`, `.Service`, `.Tests`, `.Demo` | net48 | C# 7.3 — the restrictions below apply |
| `FtsLib.Net48`, `FtsLibTest` | net48 | C# 7.3 — the restrictions below apply |
| `KitveiHakodeshService` | net10.0-windows | modern C#, AOT-published |
| `KitveiHakodeshService.Tests`, `FtsLibTest.Net10` | net10.0 | modern C# |
| `FtsLib` | net10.0 | modern C# |
| `DocumentLocator` (library) | net48 + net10.0-windows | multi-targeted — code must compile under BOTH, so guard modern syntax with `#if` |
| `DocConvertLib` | net48 + net10.0 | multi-targeted — same constraint |

In net48 / C# 7.3 projects:

- No `using` declarations (use `using` statements with braces)
- No switch expressions (use `if/else` chains)
- No nullable reference types, no records, no default interface members
- No `System.Runtime.Intrinsics` (net48 lacks it)

In the multi-targeted projects, anything not available on net48 must sit behind a `#if NET10_0_OR_GREATER` guard — a modern-only construct added unguarded breaks the net48 build.

### Build pipeline

- Vue builds as a single-file bundle via `vite-plugin-singlefile` — all JS/CSS is inlined into one `index.html`, no separate `assets/` folder
- `KitveiHakodesh.targets` is imported by the `.csproj` and runs `npm run build` after every C# build, then copies `dist/` to `bin/{Config}/KitveiHakodesh/` via robocopy
- After any Vue code change, rebuild the C# project to get the fresh bundle into `KitveiHakodesh/` — the WebView serves from that folder
- If the category tree or any data appears stuck loading in C# mode but works in dev, it is almost always a stale `KitveiHakodesh/index.html` — rebuild C# first before debugging further

### File picker / dialog rules

- `WebMessageReceived` fires on the UI thread — calling `Invoke` from inside it deadlocks (the UI thread is already busy)
- Always use `BeginInvoke` to show any dialog from a message handler — it queues the dialog to run after the handler returns
- The reply callback fires from inside the `BeginInvoke` delegate, after the user closes the dialog — this is fine, the JS Promise just waits
- For dialogs that need to send a result back to JS: use a `TaskCompletionSource<bool>` so the RPC `await handler(...)` waits for the dialog to close before the scope exits — otherwise the reply is sent after the message dispatch scope is gone

## C# Host UI in Dev Mode

- All UI that is conditional on running inside the C# WebView host (`isHosted`) must also be visible in browser dev mode
- `isHosted` and `dbReady` are exported from `src/webview-host/seforimDb.ts` as module-level constants/refs — import them from there, never recompute locally in components
- `isHosted = window.__webviewDbReady !== undefined || import.meta.env.DEV`
- **`isHosted` is therefore `true` in browser dev mode.** It means "a real data backend is available", not "running inside the WinForms host". To branch on the actual host, check `window.__webviewAction` instead. Using `isHosted` as a host-only guard is a recurring bug — it silently takes the hosted path in dev.
- `dbReady` is a `ref<boolean>` — set to `true` when the user picks a valid DB file; the `__onDbPathPicked` callback in `seforimDb.ts` handles this automatically
- Never use `typeof window.__webviewPickDbPath === 'function'` for host detection — the bridge registers those functions at Vue boot time, which is too late for module-level const evaluation
