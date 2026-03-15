---
inclusion: manual
---

# Persistence

## Architecture

All `localStorage` access goes through `src/utils/persist.ts`. Never call `localStorage` directly anywhere else.

```ts
import { persistGet, persistSet, persistRemove, clearAll, PERSIST_KEYS } from '@/utils/persist'
```

- `persistGet<T>(key, fallback)` — safe read with JSON parse, returns fallback on missing/error
- `persistSet<T>(key, value)` — JSON serialise and write
- `persistRemove(key)` — remove a single key
- `clearAll()` — wipes all keys prefixed `app.` — use for full app reset

Persistence survives across sessions (browser restarts). This is intentional — the app restores exactly where the user left off.

## Mental Model

| Scope | Who owns it | Cleared when |
|---|---|---|
| Tab list + active tab | `tabStore` | Never (always restored on next session) |
| Per-tab page state | `bookViewStore` (via `BookTabState`) | Tab is closed |
| Global book-view UI prefs (toolbar visibility, search bar position) | `bookViewStore` | Never (user-controlled reset only) |
| Global app settings | settings store (placeholder) | Never (user-controlled reset only) |
| Books FS page | — | No persisted state needed |

## Keys

All keys live in `PERSIST_KEYS` in `persist.ts`. Never use raw strings elsewhere.

| Key | Owner | Notes |
|---|---|---|
| `app.tabs` | `tabStore` | Tab list, activeTabId, nextId counter. `pdfBlobUrl` excluded (session-only blob URL) |
| `app.settings` | settings (placeholder) | Reserved, not yet used |
| `app.books.view` | `BooksFsPage` | List vs tiles view toggle |
| `app.bookView.toolbarVisible` | `bookViewStore` | Global toolbar toggle — one value for the whole app |
| `app.bookView.searchBarPos` | `bookViewStore` | Global search bar position — shared across all tabs, persists across sessions |
| `app.bookTab.<tabId>` | `bookViewStore` | Per-tab state for book-view: `bottomVisible` |

## Tab persistence

`tabStore` persists the full tab list and active tab on every mutation via a `watch`. On next session, tabs are restored exactly as left — same pages, same book IDs, same routes.

`pdfBlobUrl` is intentionally excluded — blob URLs don't survive sessions.

## Per-tab state (book-view)

`bookViewStore.getTabState(tabId)` and `setTabState(tabId, patch)` manage per-tab data keyed by tab ID.

When a tab is closed, `tabStore` fires `onTabClose` callbacks. `bookViewStore` uses this to call `persistRemove(PERSIST_KEYS.BOOK_TAB(id))`, clearing all state for that tab. Never manually remove tab keys from anywhere else.

## Pages with no persisted state

- **Books FS page** — stateless across sessions. The directory listing always reloads fresh. No keys needed.
- **Home page** — stateless. No keys needed.

## Settings persistence

Settings are global — one instance per app, not per tab. When settings are implemented, they read/write `PERSIST_KEYS.SETTINGS` directly, regardless of which tab is active.

## Adding new persisted state

1. Add a key to `PERSIST_KEYS` in `persist.ts`
2. Read with `persistGet` at store/component init; write with `persistSet` in the mutating function
3. For per-tab book-view data, add the field to `BookTabState` in `bookViewStore.ts` instead
4. Document the key in the table above

## Reset

`clearAll()` wipes all `app.*` keys. Wire to a "reset app" button in settings when implemented.
