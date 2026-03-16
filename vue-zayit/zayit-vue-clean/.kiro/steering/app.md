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
- Shared reusable components (used across features): `src/components/common/` (e.g. `AppSpinner.vue`)
- Shared composables across features: `src/composables/`
- Shared pure utils: `src/utils/`
- Pinia stores: `src/stores/` — never under `src/data/` or elsewhere

## Navigation

- Page-navigation app — navigating replaces the current tab's content in-place
- Always use `tabStore.updateActiveTab({ title, route, ...data })` for navigation — never `openTab`
- `openTab` is reserved for explicitly creating a second tab (e.g. a "new tab" button)
- Mirrors iOS navigation: forward replaces the view, back via tab history (not yet implemented)

## Design Language

- Blends iOS layout/interaction with Windows 11 Fluent Design (Fluent icons + color tokens)
- Targets small Android-type screens with touch — minimum 44px touch targets
- Compact sizing: title bar 40px, book-view toolbar 32px, list rows 44px, breadcrumb 32px, home tiles 48px icon size

### Buttons & Motion
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

### Misc UI
- Use `color-mix()` tinted backgrounds instead of solid fills for icon containers in list contexts
- Rounded corners: `4px` small controls, `8px` cards, `10–12px` search/input pills, `12px` tile icon containers
- Secondary toolbars use `var(--bg-toolbar)` — between `--bg-primary` and `--bg-secondary`
- Split pane divider hover: `color-mix(in srgb, var(--text-secondary) 25%, transparent)` — never accent color

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
| tocEntryId | INTEGER | nullable |
| chunk_id | INTEGER | nullable |

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

- All `localStorage` access goes through `src/utils/persist.ts` — never call `localStorage` directly elsewhere, except for prefix-based bulk removal (e.g. clearing all keys under a tab on close)

```ts
import { persistGet, persistSet, persistRemove, clearAll, PERSIST_KEYS } from '@/utils/persist'
```

- `persistGet<T>(key, fallback)` — safe read with JSON parse, returns fallback on missing/error
- `persistSet<T>(key, value)` — JSON serialize and write
- `persistRemove(key)` — remove a single key
- `clearAll()` — wipes all `app.*` keys (use for full app reset)

### Persisted State

| Key | Owner | Notes |
|---|---|---|
| `app.tabs` | `tabStore` | Tab list, activeTabId, nextId counter. `pdfBlobUrl` excluded (session-only) |
| `app.settings` | settings (placeholder) | Reserved, not yet used |
| `app.books.view` | `BooksFsPage` | List vs tiles view toggle |
| `app.bookView.toolbarVisible` | `bookViewStore` | Global toolbar toggle |
| `app.bookView.searchBarPos` | `bookViewStore` | Global search bar position |
| `app.bookTab.<tabId>` | `bookViewStore` | Per-tab state: `bottomVisible`, `tocVisible` |
| `app.bookTab.<tabId>.scroll.<bookId>` | `bookViewStore` | Scroll position (lineIndex) per book per tab |

All keys live in `PERSIST_KEYS` in `persist.ts` — never use raw strings elsewhere.

### Tab Persistence
- `tabStore` persists full tab list on every mutation via `watch` — restored exactly on next session
- `pdfBlobUrl` intentionally excluded — blob URLs don't survive sessions

### Per-tab State
- `bookViewStore.getTabState(tabId)` / `setTabState(tabId, patch)` manage per-tab UI state (`bottomVisible`, `tocVisible`)
- `bookViewStore.getScrollIndex(tabId, bookId)` / `setScrollIndex(tabId, bookId, index)` manage per-book scroll position
- Scroll is saved 500ms after the user stops scrolling (debounced)
- On tab close, `bookViewStore` wipes everything under `app.bookTab.<tabId>` — tab state + all scroll entries
- Navigating away from a book (opening a new book in the same tab) also clears all scroll data for that tab
- TOC opens automatically on first load of a book (set in `onSelectBook` before navigation)

### Pages with No Persisted State
- Books FS page — always reloads fresh
- Home page — stateless

### Adding New Persisted State
1. Add key to `PERSIST_KEYS` in `persist.ts`
2. Read with `persistGet` at init; write with `persistSet` in the mutating function
3. For per-tab book-view data, add field to `BookTabState` in `bookViewStore.ts`
4. For per-book scroll-like data, use the `app.bookTab.<tabId>.<scope>.<bookId>` pattern
