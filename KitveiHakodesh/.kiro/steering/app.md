# App Specification

## Overview

- Hebrew book reader — mobile-first, tabbed navigation
- Features: browse books, full-text search, book viewer, file browser
- Strictly RTL, Hebrew-only: `dir="rtl"` and `lang="he"` on root HTML — do not change
- All user-facing text must be in Hebrew — no English strings in templates, error messages, loading states, placeholders, tooltips, or any visible UI text

## Folder Structure

- `src/features/` — one folder per app feature (e.g. `book-catalog/`, `book-view/`, `full-text-search/`)
- Most feature folders keep files flat with no nested subfolders
- Exception: `book-view/` has three subfolders (`lines/`, `toc/`, `commentary/`) for organizational clarity due to complexity
- Sub-components named after parent: `BookCard.vue` → `BookCardCover.vue`, `BookCardMeta.vue`
- Shared reusable components (used across features): `src/components/`
- App shell components (title bar, page router): `src/layout/`
- Shared composables across features: `src/composables/`
- Shared pure utils: `src/utils/`
- Pinia stores: `src/stores/` — never under `src/data/` or elsewhere

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
- Always use `tabStore.updateActiveTab({ title, route, ...data })` for navigation — never `openTab`
- `openTab` is reserved for explicitly creating a second tab (e.g. a "new tab" button)
- `/book-view` and `/search` are keyed by `activeTabId` in `AppPageView` — they fully remount on tab switch, so setup-time reads of `tabStore.activeTabId` / `activeTab` are stable for the component's lifetime; all other routes share one instance

### Singleton pages

The routes `/settings`, `/books`, `/hebrewbooks`, and `/workspaces` are singleton tabs — only one tab of each may exist at a time. Always navigate to them via `tabStore.navigateToSingleton(route)`, never `updateActiveTab` or `openTab` directly. If a tab with that route already exists it switches to it (closing the current tab); otherwise the current tab is replaced in-place.

These routes are also never persisted across sessions — `persistTabs` strips them before writing to IDB.

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
- Split pane divider hover: `color-mix(in srgb, var(--text-secondary) 25%, transparent)` — never accent color
- Split pane divider visual height is 1px but touch target is 20px via a `::before` pseudo-element (`position: absolute; height: 20px; top: 50%; transform: translateY(-50%)`) — always keep this pattern on any resize handle

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

- All SQLite access goes through `src/webview-host/db.ts` — never call fetch against the DB from a component or composable
- All raw SQL strings live in `src/webview-host/queries.sql.ts` — no inline SQL anywhere else
- Feature composables call `query()` with a SQL constant from `queries.sql.ts` and a params array
- **Exception — dictionary DB**: dictionary SQL lives in `src/webview-host/dictionaryDb.ts`, not in `queries.sql.ts`. Both the C# host path (`__webviewDictQuery`) and the dev path (`devQueryDict`) execute the same SQL string sent from the frontend — there is nothing to keep in sync between C# and dev for dictionary queries.

### Transports (auto-selected at runtime)

1. C# WebView host — when `window.__webviewQuery` is injected by the host before app boots
2. HTTP dev server — fallback for browser dev

Dev server: `npm run dev:server` — `DB_PATH` sets the `.db` file (default `./data.db`), `PORT` sets port (default `4000`), override URL via `VITE_DB_URL` in `.env.development`

### Schema Reference

#### category

| column     | type    | notes                 |
| ---------- | ------- | --------------------- |
| id         | INTEGER | PK                    |
| parentId   | INTEGER | nullable, self-ref    |
| title      | TEXT    | not null              |
| level      | INTEGER | not null, default 0   |
| orderIndex | INTEGER | not null, default 999 |

#### category_closure

Closure table for category hierarchy.

| column       | type    | notes          |
| ------------ | ------- | -------------- |
| ancestorId   | INTEGER | PK (composite) |
| descendantId | INTEGER | PK (composite) |

#### author / topic / pub_place / pub_date / source

Simple lookup tables: `id` PK, `name` TEXT not null.

#### book

| column                  | type    | notes         |
| ----------------------- | ------- | ------------- |
| id                      | INTEGER | PK            |
| categoryId              | INTEGER | FK → category |
| sourceId                | INTEGER | FK → source   |
| title                   | TEXT    | not null      |
| heShortDesc             | TEXT    | nullable      |
| notesContent            | TEXT    | nullable      |
| orderIndex              | INTEGER | default 999   |
| totalLines              | INTEGER | default 0     |
| isBaseBook              | INTEGER | 0/1 bool      |
| hasTargumConnection     | INTEGER | 0/1 bool      |
| hasReferenceConnection  | INTEGER | 0/1 bool      |
| hasSourceConnection     | INTEGER | 0/1 bool      |
| hasCommentaryConnection | INTEGER | 0/1 bool      |
| hasOtherConnection      | INTEGER | 0/1 bool      |
| hasAltStructures        | INTEGER | 0/1 bool      |
| hasTeamim               | INTEGER | 0/1 bool      |
| hasNekudot              | INTEGER | 0/1 bool      |
| externalLibraryId       | INTEGER | nullable      |

#### book_pub_place / book_pub_date / book_topic / book_author

Junction tables: `bookId` + respective FK, composite PK.

#### line

| column    | type    | notes     |
| --------- | ------- | --------- |
| id        | INTEGER | PK        |
| bookId    | INTEGER | FK → book |
| lineIndex | INTEGER | not null  |
| content   | TEXT    | not null  |

#### tocText

`id` PK, `text` TEXT not null.

#### tocEntry

| column      | type    | notes               |
| ----------- | ------- | ------------------- |
| id          | INTEGER | PK                  |
| bookId      | INTEGER | FK → book           |
| parentId    | INTEGER | nullable, self-ref  |
| textId      | INTEGER | FK → tocText        |
| level       | INTEGER | not null            |
| lineId      | INTEGER | nullable, FK → line |
| isLastChild | INTEGER | 0/1 bool            |
| hasChildren | INTEGER | 0/1 bool            |

#### connection_type

`id` PK, `name` TEXT not null.

#### link

| column           | type    | notes                |
| ---------------- | ------- | -------------------- |
| id               | INTEGER | PK                   |
| sourceBookId     | INTEGER | FK → book            |
| targetBookId     | INTEGER | FK → book            |
| sourceLineId     | INTEGER | FK → line            |
| targetLineId     | INTEGER | FK → line            |
| connectionTypeId | INTEGER | FK → connection_type |

#### book_has_links

| column         | type    | notes    |
| -------------- | ------- | -------- |
| bookId         | INTEGER | PK       |
| hasSourceLinks | INTEGER | 0/1 bool |
| hasTargetLinks | INTEGER | 0/1 bool |

#### line_toc

`lineId` PK, `tocEntryId` FK → tocEntry.

#### alt_toc_structure

| column  | type    | notes     |
| ------- | ------- | --------- |
| id      | INTEGER | PK        |
| bookId  | INTEGER | FK → book |
| key     | TEXT    | not null  |
| title   | TEXT    | nullable  |
| heTitle | TEXT    | nullable  |

#### alt_toc_entry

| column      | type    | notes                  |
| ----------- | ------- | ---------------------- |
| id          | INTEGER | PK                     |
| structureId | INTEGER | FK → alt_toc_structure |
| parentId    | INTEGER | nullable, self-ref     |
| textId      | INTEGER | FK → tocText           |
| level       | INTEGER | not null               |
| lineId      | INTEGER | nullable, FK → line    |
| isLastChild | INTEGER | 0/1 bool               |
| hasChildren | INTEGER | 0/1 bool               |

#### line_alt_toc

`lineId` + `structureId` composite PK, `altTocEntryId` FK → alt_toc_entry.

#### book_acronym

`bookId` + `term` composite PK.

#### default_commentator / default_targum

`bookId` + `commentatorBookId`/`targumBookId` composite PK, `position` INTEGER not null.

#### bloom_metadata

`id` PK, `chunk_size` INTEGER not null.

## Persistence

Persistence uses three separate IndexedDB databases plus one localStorage key — all access goes through `src/utils/persistence.ts`.

### Why localStorage exists here

Opening an IndexedDB database has a measurable async cost (~65ms cold on WebView2) even for a single key read. For the app-reset flag — a boolean checked on every boot but set only when the user explicitly resets the app — this cost is unacceptable. The flag is stored in `localStorage` under key `__pendingReset` so the boot check is synchronous and zero-cost on normal launches. This is the only legitimate use of localStorage in the app; all other persistence goes through IDB.

### IDB performance rule

Every IDB open has a real async cost, especially cold on WebView2. Before adding any new boot-time IDB read, ask whether it can be deferred until after mount, batched into an existing transaction, or replaced with a synchronous localStorage read. Never add a new `await idbGet()` call to the startup sequence without justification.

For runtime hot paths (opening a book, switching tabs, every search), prefer an in-memory cache in front of IDB. The pattern is: check the cache first, read IDB on miss, populate the cache, return. Writes update the cache immediately and write to IDB fire-and-forget or via the existing pending-save promise chain.

### In-memory cache rules

When adding an in-memory cache in front of IDB, always answer these questions before shipping:

- What is the maximum number of entries this cache can hold? If unbounded, add an explicit cap.
- When are entries evicted? Every cache tied to a lifecycle (tab, session, workspace) must evict when that lifecycle ends — e.g. `_bookStateCache` entries are evicted when their tab is closed. Caches not tied to a lifecycle must have a size cap with FIFO or LRU eviction.
- Does the cache stay consistent with IDB writes? Every write path must update the cache before or alongside the IDB write — never write to IDB without also updating the cache.
- Is the cached value large? Never cache result sets, arrays of objects, or anything that scales with user data — only cache scalars, small structs, and key lists. Large data belongs in IDB only.

Current caches and their caps:

- `_bookStateCache` in `tabStore` — one entry per open tab×book; evicted on `closeTab` / `closeAllTabs`
- `_lastReadCache` in `tabStore` — capped at 200 entries, FIFO eviction
- `_booksView` in `tabStore` — single scalar, no cap needed
- `_mem` in `searchCacheStore` — **not cached in memory** — search results can be hundreds of items with snippet strings; only the LRU key list (`_lru`) is kept in memory

### localStorage rules

- All localStorage access must go through `src/utils/persistence.ts` — never call `localStorage` directly anywhere else in the codebase
- The only current localStorage key is `__pendingReset` (the app-reset flag)
- When adding a new localStorage key, add it as a named constant in `persistence.ts` alongside the IDB keys
- App reset (`idbClearAll`) must also call `localStorage.clear()` or explicitly remove every known localStorage key — reset must leave no state behind

### IDB databases

| Database            | Contents                                                         |
| ------------------- | ---------------------------------------------------------------- |
| `app-tabs`          | Tabs list, tab states, book states (workspace-scoped keys)       |
| `app-lastread`      | Per-book last-read positions (LRU-capped at 1000)                |
| `app-search-cache`  | FTS search result cache (LRU-capped at 100 queries)              |

All scalar settings (fonts, zoom, theme, toolbar state, workspaces, calendar prefs, etc.) live in localStorage via `lsGet`/`lsSet` — synchronous, zero async cost. IDB is only used for data that is too large or structured for localStorage (custom themes, tab/book state, last-read positions, search cache).

All persistence access must go through `src/utils/persistence.ts` exclusively — no component, composable, or store may call `localStorage` or any IDB API directly. This is a hard rule with no exceptions.

Components and composables must never import from `src/utils/persistence.ts` directly — they go through a store (`tabStore`, `bookViewStore`, `settingsStore`). Only stores import from `persistence.ts`.

### Key scheme

localStorage keys are prefixed with `kitvei-hakodesh.` automatically by `lsGet`/`lsSet`. The `KEYS` constants in `persistence.ts` are the unprefixed names.

`app-tabs` keys are workspace-scoped:

| Key                            | Value              |
| ------------------------------ | ------------------ |
| `tabs:{wsId}`                  | `PersistedTabList` |
| `tab:{wsId}:{tabId}`           | `TabState`         |
| `book:{wsId}:{tabId}:{bookId}` | `BookState`        |

`app-lastread` keys: `lastread:{bookId}` → `LastReadState`

### Stores

- `tabStore` — tab lifecycle, navigation, tab/book state, lastread, booksView setting, and `resetAll()`
- `bookViewStore` — toolbar/searchBarPos; reads from localStorage at init (synchronous)
- `settingsStore` — all app settings in localStorage; `init()` is synchronous
- `themeStore` — theme preset + reading background in localStorage; custom themes in IDB via `themes.ts`
- `workspaceStore` — workspace list in localStorage; `init()` is synchronous

### lastread LRU cap

Always use `tabStore.setLastReadPos()` — it calls `idbSetLastRead()` which enforces the 1000-entry cap.

### App reset

`tabStore.resetAll()` calls `idbScheduleReset()` which writes the `__pendingReset` localStorage flag, then `window.location.reload()`. On next boot, `idbCheckAndExecReset()` sees the flag synchronously, calls `lsClearAll()` to wipe all localStorage keys, deletes all IDB databases, then reloads into a clean state.

### Adding new persisted state

- Global scalar setting → add key to `KEYS` in `persistence.ts`, expose via the owning store
- Per-tab UI state → add field to `TabState` in `persistence.ts`, expose via `tabStore.getTabViewState/setTabViewState`
- Per-tab+book state → add field to `BookState` in `persistence.ts`, expose via `tabStore.getBookViewState/setBookViewState`
- Per-book global state → add field to `LastReadState` in `persistence.ts`, expose via `tabStore.getLastReadPos/setLastReadPos`
- Boot-time flag that must be synchronous → use localStorage via `persistence.ts`, add a named constant, and ensure `idbClearAll` removes it

## HebrewBooks Downloads

- HebrewBooks blocks direct HTTP downloads — all downloads must go through the WebView2 browser engine
- Never use `HttpClient` or any direct HTTP fetch to download HebrewBooks PDFs
- The download URL format is: `https://download.hebrewbooks.org/downloadhandler.ashx?req={bookId}`
- C# intercepts the browser download via `DownloadStarting` event and redirects the file path
- Open-in-viewer: redirect to cache folder, suppress dialog, push `hbPdfReady` event when complete
- Save As: show native `SaveFileDialog` to let user pick destination, browser handles the actual download

## C# Backend

- Target framework: .NET 4.8, C# 7.3 — no C# 8+ features
- No `using` declarations (use `using` statements with braces)
- No switch expressions (use `if/else` chains)
- No nullable reference types, no records, no default interface members

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
- `isHosted` and `dbReady` are exported from `src/webview-host/db.ts` as module-level constants/refs — import them from there, never recompute locally in components
- `isHosted = window.__webviewDbReady !== undefined || import.meta.env.DEV`
- `dbReady` is a `ref<boolean>` — set to `true` when the user picks a valid DB file; the `__onDbPathPicked` callback in `db.ts` handles this automatically
- Never use `typeof window.__webviewPickDbPath === 'function'` for host detection — the bridge registers those functions at Vue boot time, which is too late for module-level const evaluation
