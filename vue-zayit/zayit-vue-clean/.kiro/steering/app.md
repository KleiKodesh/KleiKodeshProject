# App Specification

## RTL Layout — Ground Rules

This app is strictly RTL. Every spatial decision must be understood in physical screen terms:

| Concept               | Physical screen position |
| --------------------- | ------------------------ |
| inline-start          | RIGHT side of screen     |
| inline-end            | LEFT side of screen      |
| Reading direction     | Right → Left             |
| First item in a row   | Appears on the RIGHT     |
| Tree/list indentation | Shifts toward the LEFT   |

### Rules for positioning and direction

- "Right" always means the physical right side of the screen
- "Left" always means the physical left side of the screen — no ambiguity, no exceptions
- Side panel on the right: `position: absolute; right: 0`
- Side panel on the left: `position: absolute; left: 0`
- Slide in from right: start at `translateX(100%)`, animate to `translateX(0)`
- Slide in from left: start at `translateX(-100%)`, animate to `translateX(0)`
- Tree chevrons: collapsed → `IconChevronLeft` (points left toward children); expanded → `IconChevronDown`
- `IconTextBulletListTree` must always have `class="rtl-flip"` (`transform: scaleX(-1)`) — it's an LTR-designed icon
- Use physical `left`/`right` for absolutely/fixed positioned overlays and panels — not `inset-inline-start/end`
- Use logical properties (`padding-inline-start`, `margin-inline-end`, etc.) only for flow content

## Overview

- Hebrew book reader — mobile-first, tabbed navigation
- Features: browse books, full-text search, book viewer, file browser
- Strictly RTL, Hebrew-only: `dir="rtl"` and `lang="he"` on root HTML — do not change
- All user-facing text must be in Hebrew — no English strings in templates, error messages, loading states, placeholders, tooltips, or any visible UI text

## Folder Structure

- `src/components/` organized by feature folder (e.g. `books-fs/`, `book-view/`, `search/`)
- Each feature folder contains its page component, composables, and sub-components — keep flat, no nested subfolders
- Sub-components named after parent: `BookCard.vue` → `BookCardCover.vue`, `BookCardMeta.vue`
- Shared reusable components (used across features): `src/components/common/`
- Shared composables across features: `src/composables/`
- Shared pure utils: `src/utils/`
- Pinia stores: `src/stores/` — never under `src/data/` or elsewhere

## Navigation

- Page-navigation app — navigating replaces the current tab's content in-place
- Always use `tabStore.updateActiveTab({ title, route, ...data })` for navigation — never `openTab`
- `openTab` is reserved for explicitly creating a second tab (e.g. a "new tab" button)
- Mirrors iOS navigation: forward replaces the view, back via tab history (not yet implemented)

### Tab interface (`Tab`)

Each tab carries both routing and in-memory state:

```ts
interface Tab {
  id: string
  title: string
  route: TabRoute // '/' | '/book-view' | '/search' | '/pdf-view' | '/settings' | '/books' | '/hebrewbooks' | '/workspaces'

  // Book reader
  bookId?: number
  tocPath?: string // current TOC breadcrumb — updated on scroll, cleared on unmount
  openToc?: boolean // in-memory only — not persisted
  openTocEntryId?: number // consumed on mount, cleared immediately after
  openTocLineIndex?: number // consumed on mount, cleared immediately after
  searchHighlightLineIndex?: number // consumed on mount, cleared immediately after
  searchHighlightQuery?: string // consumed on mount, cleared immediately after
  searchQuery?: string

  // PDF viewer
  pdfVirtualUrl?: string // in-memory only — reconstructed on restore
  pdfFileName?: string
  pdfFilePath?: string // persisted — local file path
  pdfHbBookId?: string // persisted — HebrewBooks book ID
  pdfHbBookTitle?: string // persisted — HebrewBooks book title (used as cache filename)
  pdfConverting?: boolean // in-memory only
  pdfLoadingType?: 'converting' | 'downloading' // in-memory only
}
```

Fields marked "in-memory only" are stripped before writing to IDB. Fields marked "consumed on mount" are read once by the page component then immediately cleared via `updateActiveTab({ field: undefined })` so they don't re-trigger on re-render.

### Tab store API

```ts
tabStore.updateActiveTab(patch) // navigate in-place — primary navigation method
tabStore.openTab(partial) // create a new tab and switch to it
tabStore.switchTab(id) // switch to an existing tab by id
tabStore.closeTab(id) // close a tab; cleans up its IDB keys
tabStore.closeAllTabs() // close all tabs; resets to a single home tab
tabStore.openNewHomeTab() // switch to existing '/' tab or open one
tabStore.navigateToSingleton(route) // singleton-safe navigation (see below)
```

### Component keying — when pages remount

`AppPageView` renders the active tab's page component. The `:key` behaviour controls when a page fully remounts vs reuses its instance:

- `/book-view` and `/search` — keyed by `activeTabId`: remount whenever the active tab changes, so each tab gets its own isolated component instance with its own state
- All other routes — no key: the component instance is reused across tab switches (singleton pages like `/settings`, `/books` share one instance)

This means `/book-view` and `/search` components can safely read `tabStore.activeTabId` and `tabStore.activeTab` at setup time — those values are stable for the lifetime of that instance.

### Singleton pages

The routes `/settings`, `/books`, `/hebrewbooks`, and `/workspaces` are **singleton tabs** — only one tab of each may exist at a time. Always navigate to them via `tabStore.navigateToSingleton(route)`, never `updateActiveTab` or `openTab` directly.

`navigateToSingleton` behaviour:

- If a tab with that route already exists in another tab → switch to it and close the current tab
- If not open anywhere → replace the current tab's content in-place (`updateActiveTab`)

These routes are also **never persisted** across sessions — `persistTabs` strips them before writing to IDB, so they will not be restored on next launch.

### Tab persistence

`watch([tabs, activeTabId], persistTabs, { deep: true })` — the tab list is written to IDB on every change.

What is stripped before persisting:

- All singleton-route tabs (`/settings`, `/books`, `/hebrewbooks`, `/workspaces`)
- `pdfVirtualUrl`, `pdfConverting`, `pdfLoadingType` (in-memory only)
- `openToc`, `openTocEntryId`, `openTocLineIndex`, `searchHighlightLineIndex`, `searchHighlightQuery` (consumed on mount)

If the active tab was a singleton (and thus stripped), `persistedActiveTabId` falls back to the first remaining tab.

### Scroll position save/restore for `/book-view`

Scroll state is saved to IDB (`BookState`) at three points:

1. `visibilitychange` → hidden (app backgrounded / WebView loses focus)
2. `beforeunload`
3. `onBeforeUnmount` — covers in-app tab switching where visibility never changes

On mount, `BookViewPage` reads `BookState` (tab+book scoped) and falls back to `LastReadState` (book-scoped global). Scroll is not restored if the tab was opened via TOC navigation (`openTocLineIndex` takes priority).

The emitted scroll index for TOC tracking uses the first _visible_ line (bottom edge past `scrollTop`), not the first _rendered_ item — overscan items above the fold are excluded.

## Design Language

The visual style is a deliberate blend of two design systems:

- **VSCode** provides the structural foundation: color palette, flat chrome, thin borders, muted text hierarchy, and overall density
- **Windows 11 Fluent** provides the interaction feel: `4px` rounded corners on controls, subtle depth via `color-mix()` tints, smooth motion, touch-friendly sizing, and Fluent icons throughout

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

## Vue Patterns

- Prefer `nextTick` over `setTimeout` when waiting for the DOM to update after a reactive change — `nextTick` is deterministic and tied to Vue's render cycle
- Example: focusing an input after a panel opens → `onMounted(() => nextTick(() => inputRef.value?.focus()))`

## Database

- All SQLite access goes through `src/host/db.ts` — never call fetch against the DB from a component or composable
- All raw SQL strings live in `src/host/queries.sql.ts` — no inline SQL anywhere else
- Feature composables call `query()` with strings from `queries.sql.ts`

```ts
import { query } from '@/host/db'
import { SQL } from '@/host/queries.sql'

const books = await query<Book>(SQL.GET_ALL_BOOKS)
```

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
| column | type | notes |
|---|---|---|
| ancestorId | INTEGER | PK (composite) |
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

Persistence uses three separate IndexedDB databases — one per concern. No localStorage is used anywhere. No migration code exists.

| Database       | Contents                                                    |
| -------------- | ----------------------------------------------------------- |
| `app-settings` | All scalar settings (one key per setting, no prefix needed) |
| `app-tabs`     | Tabs list, tab states, book states (workspace-scoped keys)  |
| `app-lastread` | Per-book last-read positions (LRU-capped at 1000)           |

**All IDB access must go through `src/utils/idbPersistence.ts` exclusively — no component, composable, or store may call any IDB API directly. This is a hard rule with no exceptions.**

Components and composables must never import from `src/utils/idbPersistence.ts` directly — they go through a store (`tabStore`, `bookViewStore`, `settingsStore`). Only stores import from `idbPersistence.ts`.

### Key scheme

`app-settings` keys are plain strings (no prefix): `headerFont`, `theme`, `customThemes`, etc.

`app-tabs` keys remain workspace-scoped:
| Key | Value |
|---|---|
| `tabs:{wsId}` | `PersistedTabList` |
| `tab:{wsId}:{tabId}` | `TabState` |
| `book:{wsId}:{tabId}:{bookId}` | `BookState` |

`app-lastread` keys: `lastread:{bookId}` → `LastReadState`

### Stores

- `tabStore` — tab lifecycle, navigation, tab/book state, lastread, booksView setting, and `resetAll()`
- `bookViewStore` — toolbar/zoom/searchBarPos; reads from IDB at init, writes via `idbPersistence.ts`
- `settingsStore` — app settings; each setting has its own key in `app-settings`, loaded in parallel at init, each watch writes only its own key
- `themeStore` — theme preset + reading background; stored at key `theme`; custom themes at `customThemes` via `themes.ts`

### lastread LRU cap

Always use `tabStore.setLastReadPos()` — it calls `idbSetLastRead()` which enforces the 1000-entry cap.

### App reset

`tabStore.resetAll()` calls `idbClearAll()` which deletes all three databases, then the caller does `window.location.reload()`. The "איפוס האפליקציה" tab in `SettingsPage.vue` triggers this.

### Adding new persisted state

- Global scalar setting → add key to `KEYS` in `idbPersistence.ts`, expose via the owning store
- Per-tab UI state → add field to `TabState` in `idbPersistence.ts`, expose via `tabStore.getTabViewState/setTabViewState`
- Per-tab+book state → add field to `BookState` in `idbPersistence.ts`, expose via `tabStore.getBookViewState/setBookViewState`
- Per-book global state → add field to `LastReadState` in `idbPersistence.ts`, expose via `tabStore.getLastReadPos/setLastReadPos`

## Dropdowns

- Always anchor the top-left corner of a dropdown to the left edge of its toggle — `position: absolute; top: calc(100% + 4px); left: 0` — so it flows rightward and stays within the viewport naturally
- Never anchor to `right: 0` (that would push it off the left edge of the screen in RTL)
- Extract every dropdown to its own component — never inline dropdown markup in a parent component

## Virtual Scroller Keyboard Navigation

Every component that uses `useVirtualizer` from `@tanstack/vue-virtual` **must** wire up `useVirtualScrollerKeys` from `@/composables/useVirtualScrollerKeys`.

```ts
import { useVirtualScrollerKeys } from '@/composables/useVirtualScrollerKeys'

useVirtualScrollerKeys(
  scrollerEl,
  () =>
    virtualizer.value as unknown as import('@tanstack/vue-virtual').Virtualizer<Element, Element>,
  () => items.value.length,
)
```

- The scroll container element must have `tabindex="0"` so it can receive keyboard focus.
- Ctrl+Home scrolls to the first item then sets `scrollTop = 0`.
- Ctrl+End scrolls to the last item then sets `scrollTop = scrollHeight`.
- The composable uses `useEventListener` from VueUse and cleans up automatically.

## HebrewBooks Downloads

- HebrewBooks blocks direct HTTP downloads — all downloads **must go through the WebView2 browser engine**
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

- Vue builds as a **single-file bundle** via `vite-plugin-singlefile` — all JS/CSS is inlined into one `index.html`, no separate `assets/` folder
- `ZayitVue.targets` is imported by the `.csproj` and runs `npm run build` after every C# build, then copies `dist/` to `bin/{Config}/kezayit/` via robocopy
- After any Vue code change, **rebuild the C# project** to get the fresh bundle into `kezayit/` — the WebView serves from that folder
- If the category tree or any data appears stuck loading in C# mode but works in dev, it is almost always a stale `kezayit/index.html` — rebuild C# first before debugging further

### File picker / dialog rules

- `WebMessageReceived` fires on the UI thread — calling `Invoke` from inside it deadlocks (the UI thread is already busy)
- Always use `BeginInvoke` to show any dialog from a message handler — it queues the dialog to run after the handler returns
- The reply callback fires from inside the `BeginInvoke` delegate, after the user closes the dialog — this is fine, the JS Promise just waits
- For dialogs that need to send a result back to JS: use a `TaskCompletionSource<bool>` so the RPC `await handler(...)` waits for the dialog to close before the scope exits — otherwise the reply is sent after the message dispatch scope is gone

## C# Host UI in Dev Mode

- All UI that is conditional on running inside the C# WebView host (`isHosted`) must also be visible in browser dev mode
- `isHosted` and `dbReady` are exported from `src/host/db.ts` as module-level constants/refs — import them from there, never recompute locally in components
- `isHosted = window.__webviewDbReady !== undefined || import.meta.env.DEV`
- `dbReady` is a `ref<boolean>` — set to `true` when the user picks a valid DB file; the `__onDbPathPicked` callback in `db.ts` handles this automatically
- Never use `typeof window.__webviewPickDbPath === 'function'` for host detection — the bridge registers those functions at Vue boot time, which is too late for module-level const evaluation
