# Code Quality Refactor Plan

Analysis of responsibility distribution, repetition, and architectural issues found across the Vue frontend.

---

## Issue 1 — `useAppNavigation.ts`: duplicated logic between `navigate` and `navigateInNewTab` ✅ Done

Extracted `handleFilePicker(newTab)`, `handleExternalLink()`, and `handleDbPicker()` as private helpers. Both `navigate` and `navigateInNewTab` call them — no more copy-pasted branches.

---

## Issue 2 — `settingsStore.ts`: noisy `init()` and repetitive watch pattern ✅ Done

Added `loadSetting<T>(key, target)` and `persistSetting(ref, key, afterSave?)` helpers. `init()` is now 20 one-liners instead of 40. The watch block is 17 `persistSetting(...)` calls instead of 17 inline `watch(ref, (v) => { lsSet(...); ... })` blocks.

---

## Issue 3 — `BookViewPage.vue`: too much orchestration logic in the component ✅ Done

Extracted three composables:
- `useTocScrollTracking.ts` — owns `tocScrolling`, `tocScrollTargetLineIndex`, `tocScrollTimer`; exposes `beginTocScroll(entry)` and `checkTocScrollProgress(lineIndex)`
- `usePinnedCommentary.ts` — owns `defaultCommentatorBookIds` fetch, `pinnedCommentaryBookId`, and both fallback watches
- `useCommentaryNavigation.ts` — owns `onNavigateSection`, `pendingNavStop`, and the commentary-loading watch; supports both TOC mode and normal mode

`BookViewPage.vue` script setup reduced from ~350 lines to ~280 lines.

---

## Issue 4 — `pdfStore.ts`: direct `Object.assign(tab, ...)` mutations bypass `tabStore` ✅ Done

Added `updateTab(tabId, patch)` to `tabStore`. All 6 `Object.assign(tab, ...)` calls in `pdfStore` replaced with `tabStore.updateTab(tabId, ...)`. Removed the now-unnecessary `_closeTab` wrapper.

---

## Issue 5 — `useCommentary.ts`: unbounded module-level cache ✅ Done

Added `STATIC_FILTER_CACHE_MAX = 50` cap and a `cacheSet()` helper that evicts the oldest entry (FIFO) when the cap is reached. The cache key is `sourceBookId` so 50 entries covers any realistic session.

---

## Issue 6 — `booksDataStore.ts`: imports from a component folder ✅ Done

Moved `booksCategoryTree.ts` to `src/utils/booksCategoryTree.ts`. Updated all import sites across `books-fs/`, `search-db/`, `book-view/`, `home/`, and `stores/`. Updated `src/utils/README.md`.

---

## Issue 7 — `persistence.ts`: HebrewBooks history logic doesn't belong here ✅ Done

Moved `HbHistoryEntry`, `openHbHistoryDb`, `idbHbGetHistory`, and `idbHbTrackAccess` into `hebrewBooksHistoryStore.ts`. The store now owns its own IDB database entirely. `persistence.ts` calls `dropHbHistoryDb()` (exported from the store as a plain function) via a lazy import to avoid circular dependency. Removed `'app-hb-history'` from the `handles` map in `persistence.ts`.

---

## Issue 8 — `useBloomSearch.ts`: debug `console.log` statements in production paths ✅ Done

Five `console.log` calls with `[search]` prefixes logged cache hit/miss details on every search execution. Removed. `console.error` calls kept.

**File:** `src/components/search-db/useBloomSearch.ts`

---

## Issue 9 — `useToolbarPosition.ts`: a file that exports only a type ✅ Done

Type inlined into `src/stores/bookViewStore.ts` as `export type ToolbarPosition`. File deleted. README and architecture.md updated.

**Files:** `src/composables/useToolbarPosition.ts` (deleted), `src/stores/bookViewStore.ts`

---

## Issue 10 — `BookViewPage.vue` template: `BookViewToolbar` rendered 4 times ✅ Done

Reduced from 4 instances to 2:
- One outside `body-row` for top/bottom positions (guarded by `toolbarPosition === 'top'` and `=== 'bottom'` respectively)
- One inside `body-row` for right/left positions (single instance, `toolbar-order-end` CSS class pushes it to physical left when `toolbarPosition === 'left'`)

**File:** `src/components/book-view/BookViewPage.vue`

---

## Priority Order

| # | Issue | Risk if left | Effort |
|---|---|---|---|
| 4 | `pdfStore` direct tab mutation | Correctness — silent bypass of tabStore contract | Low |
| 6 | `booksDataStore` imports from components | Architecture inversion | Medium |
| 7 | `persistence.ts` HB history leak | Separation of concerns | Medium |
| 1 | `useAppNavigation` duplication | Maintainability — two places to update on every nav change | Low |
| 2 | `settingsStore` init/watch noise | Readability | Low |
| 5 | Unbounded cache in `useCommentary` | Correctness — stale data after workspace switch | Low |
| 3 | `BookViewPage` over-orchestration | Complexity — at hard line limit, blocks future work | High |
| 10 | 4× toolbar in template | Template noise | Low |
| 9 | `useToolbarPosition.ts` single-type file | Noise | Trivial |
| 8 | Debug `console.log` in search | Polish | Trivial |
