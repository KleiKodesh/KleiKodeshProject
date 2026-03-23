# Persistence

## Overview

All persistence uses a single **IndexedDB** database (`app-state`, version 1) with one object store and prefixed string keys. No localStorage is used anywhere. No migration code exists.

**All IDB access goes through `src/utils/idb.ts` exclusively. Only stores may import from `idb.ts` — components and composables go through a store.**

---

## Key Scheme

| Prefix | Key | Value | Owner |
|---|---|---|---|
| `settings:` | `settings:books.view` | `'list'\|'tiles'\|'tree'` | `tabStore` |
| `settings:` | `settings:bookView.toolbarVisible` | `boolean` | `bookViewStore` |
| `settings:` | `settings:bookView.searchBarPos` | `{x,y}` | `bookViewStore` |
| `settings:` | `settings:bookView.zoom` | `number` | `bookViewStore` |
| `settings:` | `settings:censorDivineNames` | `boolean` | `settingsStore` |
| `settings:` | `settings:diacriticsState` | `number` | `settingsStore` |
| `settings:` | `settings:headerFont` | `string` | `settingsStore` |
| `settings:` | `settings:textFont` | `string` | `settingsStore` |
| `settings:` | `settings:fontSize` | `number` | `settingsStore` |
| `settings:` | `settings:linePadding` | `number` | `settingsStore` |
| `settings:` | `settings:commentaryHeaderFont` | `string` | `settingsStore` |
| `settings:` | `settings:commentaryTextFont` | `string` | `settingsStore` |
| `settings:` | `settings:commentaryFontSize` | `number` | `settingsStore` |
| `settings:` | `settings:commentaryLinePadding` | `number` | `settingsStore` |
| `settings:` | `settings:useSeparateCommentarySettings` | `boolean` | `settingsStore` |
| `settings:` | `settings:appZoom` | `number` | `settingsStore` |
| `settings:` | `settings:newTabPage` | `NewTabPage` | `settingsStore` |
| `settings:` | `settings:pdfPageFilters` | `boolean` | `settingsStore` |
| `settings:` | `settings:resumeLastRead` | `boolean` | `settingsStore` |
| `settings:` | `settings:theme` | `{themePreset, readingBackground}` | `themeStore` |
| `settings:` | `settings:customThemes` | `Record<string, Theme>` | `themes.ts` |
| `tabs:` | `tabs:list` | `PersistedTabList` | `tabStore` |
| `tab:` | `tab:{tabId}` | `TabState` | `tabStore` |
| `book:` | `book:{tabId}:{bookId}` | `BookState` | `tabStore` |
| `lastread:` | `lastread:{bookId}` | `LastReadState` | `tabStore` (LRU cap 1000) |

---

## Data Shapes

```ts
interface TabState {
  bottomVisible: boolean
}

interface BookState {
  scrollIndex: number
  scrollOffset: number
  selectedLineId?: number | null
  commentaryScrollIndex?: number | null
  commentaryScrollOffset?: number | null
}

interface LastReadState {  // same fields, no tab association
  scrollIndex: number
  scrollOffset: number
  selectedLineId?: number | null
  commentaryScrollIndex?: number | null
  commentaryScrollOffset?: number | null
}
```

---

## Restore Priority (BookViewPage mount)

1. `tab:{tabId}` → restore `bottomVisible`
2. `book:{tabId}:{bookId}` (tab-specific, preferred) or fall back to `lastread:{bookId}`
3. Restore main scroll via virtualizer
4. Restore `selectedLineId` → triggers commentary load
5. After commentary loads → restore commentary scroll via virtualizer

---

## Write Triggers

| Event | Written to |
|---|---|
| Scroll (main view) | `book:` + `lastread:` (100ms debounce) |
| `selectedLineId` changes | `book:` + `lastread:` (100ms debounce) |
| Commentary scrolls | `book:` + `lastread:` (100ms debounce) |
| `onBeforeUnmount` | `book:` + `lastread:` (immediate) |
| `bottomVisible` changes | `tab:` (immediate) |
| Tab closed | delete `tab:{id}` + all `book:{id}:*` |

---

## Lifecycle (main.ts)

All stores init in parallel before `app.mount()`:

```ts
await Promise.all([
  tabStore.init(),        // loads tabs:list
  bookViewStore.init(),   // loads toolbar/zoom/searchBarPos
  settingsStore.init(),   // loads all settings: keys in parallel
  themeStore.init(),      // loads settings:theme
  loadCustomThemes(),     // loads settings:customThemes
])
```

---

## App Reset

`tabStore.resetAll()` calls `idbClearAll()` which wipes the entire store. The "איפוס האפליקציה" tab in `SettingsPage.vue` calls `tabStore.resetAll()` then `window.location.reload()`.

---

## Adding New Persisted State

| What | Where | How |
|---|---|---|
| Global scalar setting | Add key to `KEYS` in `idb.ts` | Use `idbGet`/`idbSet` in the owning store |
| Per-tab UI state | Add field to `TabState` in `idb.ts` | Expose via `tabStore.getTabViewState/setTabViewState` |
| Per-tab+book state | Add field to `BookState` in `idb.ts` | Expose via `tabStore.getBookViewState/setBookViewState` |
| Per-book global state | Add field to `LastReadState` in `idb.ts` | Expose via `tabStore.getLastReadPos/setLastReadPos` |
