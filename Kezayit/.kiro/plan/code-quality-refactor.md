# Code Quality Refactor Plan

Analysis of responsibility distribution, repetition, and architectural issues found across the Vue frontend.

---

## Issue 1 — `useAppNavigation.ts`: duplicated logic between `navigate` and `navigateInNewTab`

Both functions share identical label-matching logic for singleton routing, file picker, and external links. The only difference is whether they open in-place or in a new tab. The shared branches (`'התקן כזית'`, `'בחר מסד נתונים'`, `'פתח קובץ'`) are copy-pasted verbatim.

**Fix:** Extract the shared side-effect actions into a private helper. Both `navigate` and `navigateInNewTab` call it with a `newTab: boolean` flag. No behavior change — just deduplication.

**File:** `src/composables/useAppNavigation.ts`

---

## Issue 2 — `settingsStore.ts`: noisy `init()` and repetitive watch pattern

`init()` has 20 identical `const x = v<T>(KEY); if (x != null) ref.value = x` lines. Below it, 20 individual `watch(ref, (v) => lsSet(KEY, v))` calls repeat the same pattern.

**Fix:** Two small helpers:
- `loadSetting<T>(key, target)` — reads from localStorage and assigns to the ref in one line
- `persistSetting(ref, key)` — wraps the watch + lsSet call

Cuts the noise in half with no behavior change.

**File:** `src/stores/settingsStore.ts`

---

## Issue 3 — `BookViewPage.vue`: too much orchestration logic in the component

The `<script setup>` block is 350+ lines and handles too many distinct concerns inline:

- Scroll state restoration
- Commentary pin logic (`pinnedCommentaryBookId`, `defaultCommentatorBookIds` fetch)
- TOC scroll tracking (`tocScrolling`, `tocScrollTimer` state machine)
- Search mode switching
- Section navigation (`onNavigateSection`, `pendingNavStop` pattern — 50+ lines)
- Auto-select timer management

The component is at its hard limit and the logic density makes it hard to reason about.

**Fix:** Extract at minimum:
- `useCommentaryNavigation(...)` — owns `onNavigateSection`, `pendingNavStop`, and the commentary-loading watch
- `usePinnedCommentary(...)` — owns `defaultCommentatorBookIds` fetch, `pinnedCommentaryBookId`, and the fallback watch
- `useTocScrollTracking(...)` — owns `tocScrolling`, `tocScrollTargetLineIndex`, `tocScrollTimer`

**File:** `src/components/book-view/BookViewPage.vue`

---

## Issue 4 — `pdfStore.ts`: direct `Object.assign(tab, ...)` mutations bypass `tabStore`

The PDF store mutates tab objects directly via `Object.assign(tab, ...)` in 6 places instead of going through `tabStore`. This breaks the single-writer contract — `tabStore` is supposed to own all tab mutations. If `tabStore` ever adds validation or side effects to tab updates, `pdfStore` silently bypasses them.

**Fix:** Add `tabStore.updateTab(tabId: string, patch: Partial<Omit<Tab, 'id'>>)` — same as `updateActiveTab` but targets any tab by ID. Replace all `Object.assign(tab, ...)` calls in `pdfStore` with this method.

**Files:** `src/stores/tabStore.ts`, `src/stores/pdfStore.ts`

---

## Issue 5 — `useCommentary.ts`: unbounded module-level cache

`_staticFilterGroupsCache` is a module-level `Map` that lives for the entire session and is never cleared or capped. If the user switches workspaces or resets the app without a full reload, stale data is served. The architecture rules require all module-level caches to be bounded.

**Fix:** Either move the cache into `booksDataStore` (where it can be cleared on workspace switch) or add an explicit size cap with eviction. The cache key is `sourceBookId` — one entry per book opened, so in practice it stays small, but the cap should be explicit.

**File:** `src/components/book-view/useCommentary.ts`

---

## Issue 6 — `booksDataStore.ts`: imports from a component folder

`booksDataStore` imports `buildTree`, `assignFullPaths`, `findCategoryMeta`, and the `BookRow`/`CategoryRow`/`CategoryNode` types from `@/components/books-fs/booksCategoryTree`. A store importing from a component folder inverts the dependency direction — stores should be consumed by components, not the reverse.

`booksCategoryTree.ts` is pure data logic with no Vue dependencies. It belongs in `src/utils/` (or a dedicated `src/data/` layer if it grows).

**Fix:** Move `booksCategoryTree.ts` to `src/utils/booksCategoryTree.ts`. Update all imports. Update the `src/utils/README.md`.

**Files:** `src/components/books-fs/booksCategoryTree.ts` → `src/utils/booksCategoryTree.ts`

---

## Issue 7 — `persistence.ts`: HebrewBooks history logic doesn't belong here

The HebrewBooks history DB has its own object store schema (`history` with a `lastAccessed` index), its own `openHbHistoryDb` function, and its own `HbHistoryEntry` type — all inside the shared persistence layer. It's the only DB in the file that deviates from the generic `data` store pattern. This is a feature-specific concern that leaked into the shared layer.

**Fix:** Move `HbHistoryEntry`, `openHbHistoryDb`, `idbHbGetHistory`, and `idbHbTrackAccess` into `src/stores/hebrewBooksHistoryStore.ts` (which already owns this feature). Keep `persistence.ts` as the generic IDB/localStorage layer only.

**Files:** `src/utils/persistence.ts`, `src/stores/hebrewBooksHistoryStore.ts`

---

## Issue 8 — `useBloomSearch.ts`: debug `console.log` statements in production paths

Five `console.log` calls with `[search]` prefixes log cache hit/miss details on every search execution. These are debug traces that should not ship in production.

**Fix:** Delete all `console.log` calls in `useBloomSearch.ts`. Keep `console.error` calls — those are legitimate error reporting.

**File:** `src/components/search-db/useBloomSearch.ts`

---

## Issue 9 — `useToolbarPosition.ts`: a file that exports only a type

This file exists solely to export `type ToolbarPosition = 'top' | 'bottom' | 'left' | 'right'`. A single type export does not justify its own file. The type is consumed by `bookViewStore` and `useZoom`.

**Fix:** Move the type into `src/stores/bookViewStore.ts` where the actual state lives. Delete `useToolbarPosition.ts`. Update the two import sites.

**Files:** `src/composables/useToolbarPosition.ts`, `src/stores/bookViewStore.ts`

---

## Issue 10 — `BookViewPage.vue` template: `BookViewToolbar` rendered 4 times

The template renders `<BookViewToolbar>` four times (top, right, left, bottom positions) with identical props and events, each guarded by a `v-if` on `toolbarPosition`. This is the most visually noisy part of the template.

**Fix:** Render `<BookViewToolbar>` once. Pass `toolbarPosition` as a prop and let the toolbar component (or its parent wrapper) handle its own CSS positioning. This removes ~40 lines of duplicated template markup.

**Files:** `src/components/book-view/BookViewPage.vue`, `src/components/book-view/BookViewToolbar.vue`

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
