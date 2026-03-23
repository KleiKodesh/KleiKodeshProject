# App Specification

## RTL Layout — Ground Rules

This app is strictly RTL. Every spatial decision must be understood in physical screen terms:

| Concept | Physical screen position |
|---|---|
| inline-start | RIGHT side of screen |
| inline-end | LEFT side of screen |
| Reading direction | Right → Left |
| First item in a row | Appears on the RIGHT |
| Tree/list indentation | Shifts toward the LEFT |

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

### Singleton pages

The routes `/settings`, `/books`, and `/hebrewbooks` are **singleton tabs** — only one tab of each may exist at a time. Always navigate to them via `tabStore.navigateToSingleton(route)`, never `updateActiveTab` or `openTab` directly. If a tab with that route already exists, it switches to it; otherwise the current tab is replaced.

These routes are also **never persisted** across sessions — `persistTabs` strips them before writing to IDB, so they will not be restored on next launch.

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

### Search Bar
- Always anchored to bottom of screen, iOS-style — never at top
- Use `margin-top: auto` or flex column to push it down
- iOS pill input: `border-radius: 10px`, muted fill `color-mix(in srgb, var(--text-secondary) 12%, transparent)`, icon inline, no outer border
- Clear button desaturated: `.search-input::-webkit-search-cancel-button { filter: grayscale(1) opacity(0.4) }`

### Search Inputs
- All search input containers use `border-radius: 6px` — Windows 11 Fluent style
- Exception: the bottom search bar in `BooksFsPage` uses `border-radius: 10px` (iOS pill, per its anchored-to-bottom design)
- Never use `border-radius: 0` on search inputs or their wrapper containers

### Book View — Commentary & TOC Compact Sizing

These components live inside the split-pane bottom panel and TOC side panel, where vertical space is at a premium. They intentionally use tighter sizing than the rest of the app:

| Element | Size | Notes |
|---|---|---|
| `CommentaryHeader` row height | 32px | vs 40px title bar |
| `CommentaryHeader` buttons | 24×24px, 14px icons | vs 32×32px elsewhere |
| `CommentaryHeader` title font | 13px | vs 14px standard |
| `CommentaryHeaderNav` row height | 32px | matches header |
| `CommentaryHeaderNav` buttons | 24×24px, 14px icons | same as header |
| `CommentaryHeaderNav` search input | 20px tall, 11px font | compact pill |
| TOC search input | 12px font, `padding: 4px 8px` | bottom of TOC panel |

Both `CommentaryHeader` and `CommentaryHeaderNav` use `background: var(--bg-primary)` — same as the commentary view content — so they blend in rather than standing out as a distinct toolbar.

### Misc UI
- Use `color-mix()` tinted backgrounds instead of solid fills for icon containers in list contexts
- Rounded corners: `4px` small controls, `8px` cards, `10–12px` search/input pills, `12px` tile icon containers
- Secondary toolbars use `var(--bg-toolbar)` — between `--bg-primary` and `--bg-secondary`
- Split pane divider hover: `color-mix(in srgb, var(--text-secondary) 25%, transparent)` — never accent color
- Split pane divider visual height is 1px but touch target is 20px via a `::before` pseudo-element (`position: absolute; height: 20px; top: 50%; transform: translateY(-50%)`) — always keep this pattern on any resize handle

## Vue Patterns

- Prefer `nextTick` over `setTimeout` when waiting for the DOM to update after a reactive change — `nextTick` is deterministic and tied to Vue's render cycle
- Example: focusing an input after a panel opens → `onMounted(() => nextTick(() => inputRef.value?.focus()))`

## Database

- All SQLite access goes through `src/db/db.ts` — never call fetch against the DB from a component or composable
- All raw SQL strings live in `src/db/queries.sql.ts` — no inline SQL anywhere else
- Feature composables call `query()` with strings from `queries.sql.ts`

```ts
import { query } from '@/db/db'
import { SQL } from '@/db/queries.sql'

const books = await query<Book>(SQL.GET_ALL_BOOKS)
```

### Transports (auto-selected at runtime)
1. C# WebView host — when `window.__webviewQuery` is injected by the host before app boots
2. HTTP dev server — fallback for browser dev

Dev server: `npm run dev:server` — `DB_PATH` sets the `.db` file (default `./data.db`), `PORT` sets port (default `4000`), override URL via `VITE_DB_URL` in `.env.development`

### Schema Reference

#### category
| column | type | notes |
|---|---|---|
| id | INTEGER | PK |
| parentId | INTEGER | nullable, self-ref |
| title | TEXT | not null |
| level | INTEGER | not null, default 0 |
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
| column | type | notes |
|---|---|---|
| id | INTEGER | PK |
| categoryId | INTEGER | FK → category |
| sourceId | INTEGER | FK → source |
| title | TEXT | not null |
| heShortDesc | TEXT | nullable |
| notesContent | TEXT | nullable |
| orderIndex | INTEGER | default 999 |
| totalLines | INTEGER | default 0 |
| isBaseBook | INTEGER | 0/1 bool |
| hasTargumConnection | INTEGER | 0/1 bool |
| hasReferenceConnection | INTEGER | 0/1 bool |
| hasSourceConnection | INTEGER | 0/1 bool |
| hasCommentaryConnection | INTEGER | 0/1 bool |
| hasOtherConnection | INTEGER | 0/1 bool |
| hasAltStructures | INTEGER | 0/1 bool |
| hasTeamim | INTEGER | 0/1 bool |
| hasNekudot | INTEGER | 0/1 bool |
| externalLibraryId | INTEGER | nullable |

#### book_pub_place / book_pub_date / book_topic / book_author
Junction tables: `bookId` + respective FK, composite PK.

#### line
| column | type | notes |
|---|---|---|
| id | INTEGER | PK |
| bookId | INTEGER | FK → book |
| lineIndex | INTEGER | not null |
| content | TEXT | not null |

#### tocText
`id` PK, `text` TEXT not null.

#### tocEntry
| column | type | notes |
|---|---|---|
| id | INTEGER | PK |
| bookId | INTEGER | FK → book |
| parentId | INTEGER | nullable, self-ref |
| textId | INTEGER | FK → tocText |
| level | INTEGER | not null |
| lineId | INTEGER | nullable, FK → line |
| isLastChild | INTEGER | 0/1 bool |
| hasChildren | INTEGER | 0/1 bool |

#### connection_type
`id` PK, `name` TEXT not null.

#### link
| column | type | notes |
|---|---|---|
| id | INTEGER | PK |
| sourceBookId | INTEGER | FK → book |
| targetBookId | INTEGER | FK → book |
| sourceLineId | INTEGER | FK → line |
| targetLineId | INTEGER | FK → line |
| connectionTypeId | INTEGER | FK → connection_type |

#### book_has_links
| column | type | notes |
|---|---|---|
| bookId | INTEGER | PK |
| hasSourceLinks | INTEGER | 0/1 bool |
| hasTargetLinks | INTEGER | 0/1 bool |

#### line_toc
`lineId` PK, `tocEntryId` FK → tocEntry.

#### alt_toc_structure
| column | type | notes |
|---|---|---|
| id | INTEGER | PK |
| bookId | INTEGER | FK → book |
| key | TEXT | not null |
| title | TEXT | nullable |
| heTitle | TEXT | nullable |

#### alt_toc_entry
| column | type | notes |
|---|---|---|
| id | INTEGER | PK |
| structureId | INTEGER | FK → alt_toc_structure |
| parentId | INTEGER | nullable, self-ref |
| textId | INTEGER | FK → tocText |
| level | INTEGER | not null |
| lineId | INTEGER | nullable, FK → line |
| isLastChild | INTEGER | 0/1 bool |
| hasChildren | INTEGER | 0/1 bool |

#### line_alt_toc
`lineId` + `structureId` composite PK, `altTocEntryId` FK → alt_toc_entry.

#### book_acronym
`bookId` + `term` composite PK.

#### default_commentator / default_targum
`bookId` + `commentatorBookId`/`targumBookId` composite PK, `position` INTEGER not null.

#### bloom_metadata
`id` PK, `chunk_size` INTEGER not null.

## Persistence

All persistence uses a single **IndexedDB** database (`app-state`, version 1) with one object store and prefixed string keys. No localStorage is used anywhere. No migration code exists.

**All IDB access must go through `src/utils/idb.ts` exclusively — no component, composable, or store may import from `indexedDB` directly or call any IDB API directly. This is a hard rule with no exceptions.**

Components and composables must never import from `src/utils/idb.ts` directly — they go through a store (`tabStore`, `bookViewStore`, `settingsStore`). Only stores import from `idb.ts`.

### Key scheme

| Prefix | Example key | Value | Notes |
|---|---|---|---|
| `settings:` | `settings:headerFont` | scalar | One key per setting — never a blob |
| `tabs:` | `tabs:list` | `PersistedTabList` | Tab list + activeTabId + nextId |
| `tab:` | `tab:{tabId}` | `TabState` | Per-tab UI state (`bottomVisible` only) |
| `book:` | `book:{tabId}:{bookId}` | `BookState` | Per-tab+book reading state |
| `lastread:` | `lastread:{bookId}` | `LastReadState` | Global per-book resume, LRU-capped at 1000 |

Both `book:` and `lastread:` are persisted across sessions. `book:` is tab-specific (wiped when tab closes). `lastread:` is the fallback when opening a book in a new tab or after the original tab was closed.

### Stores

- `tabStore` — tab lifecycle, navigation, tab/book state, lastread, booksView setting, and `resetAll()`
- `bookViewStore` — toolbar/zoom/searchBarPos; reads from IDB at init, writes via `idb.ts`
- `settingsStore` — app settings; each setting has its own `settings:` key, loaded in parallel at init, each watch writes only its own key
- `themeStore` — theme preset + reading background; stored as one object at `settings:theme`; custom themes at `settings:customThemes` via `themes.ts`

### lastread LRU cap

Always use `tabStore.setLastReadPos()` — it calls `idbSetLastRead()` which enforces the 1000-entry cap.

### App reset

`tabStore.resetAll()` calls `idbClearAll()` then the caller does `window.location.reload()`. The "איפוס האפליקציה" tab in `SettingsPage.vue` triggers this.

### Adding new persisted state
- Global scalar setting → add key to `KEYS` in `idb.ts`, expose via the owning store
- Per-tab UI state → add field to `TabState` in `idb.ts`, expose via `tabStore.getTabViewState/setTabViewState`
- Per-tab+book state → add field to `BookState` in `idb.ts`, expose via `tabStore.getBookViewState/setBookViewState`
- Per-book global state → add field to `LastReadState` in `idb.ts`, expose via `tabStore.getLastReadPos/setLastReadPos`

## Dropdowns

- Always anchor the top-left corner of a dropdown to the left edge of its toggle — `position: absolute; top: calc(100% + 4px); left: 0` — so it flows rightward and stays within the viewport naturally
- Never anchor to `right: 0` (that would push it off the left edge of the screen in RTL)
- Extract every dropdown to its own component — never inline dropdown markup in a parent component
