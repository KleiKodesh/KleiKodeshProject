# Performance & Architecture Audit Prompt

Paste this entire prompt into a new conversation to run a full audit.

---

You are auditing a Vue 3 + TypeScript Hebrew book-reader app for performance and architectural problems. The app is mobile-first, RTL, uses TanStack Virtual for scrolling, Pinia stores, and IndexedDB via a custom `idbPersistence.ts` layer.

## What was already fixed (do not re-report)

1. `BookViewLinesContent.vue` — scroll position was not saved on in-app tab switch because `savePos()` was only wired to `visibilitychange` / `beforeunload`. Fixed by adding `onBeforeUnmount(savePos)`.
2. `useToc.ts` / `BookViewTocTree.vue` — two separate problems:
   - `getTocPath()` rebuilt a full `new Map()` from all entries and walked the parent chain on **every scroll event**. Fixed by pre-building a `tocPathMap: Map<number, string>` once when entries load.
   - `BookViewTocTree` called `useToc()` independently, causing a duplicate DB fetch for data already loaded in `BookViewPage`. Fixed by passing `tocEntries`, `altTocSections`, `loading`, `error` as props from the parent.

---

## Audit categories — check every file listed below for ALL of these

### 1. Duplicate data fetching

- Any composable that calls `useToc()`, `useLines()`, `useCommentary()`, or any other data-fetching composable when the same data is already available in a parent and could be passed as props
- Any component that fetches data it could receive from its parent
- Any store that is initialized multiple times for the same data

### 2. Work done inside hot paths (scroll handlers, computed properties called on every render)

- `computed()` that creates a `new Map()`, `new Set()`, sorts an array, or does O(n) traversal — these should be derived once and cached, not rebuilt on every access
- Functions called from scroll/virtualizer callbacks that do more than O(1) work
- `watch` handlers with `{ deep: true }` on large reactive arrays where a shallower watch would suffice
- Any `.find()`, `.filter()`, `.map()`, or `.reduce()` inside a `computed` that iterates the full dataset on every reactive dependency change

### 3. Reactive over-triggering

- `watch` on a large array/object with `deep: true` when only a specific field changes
- `computed` that depends on a large reactive collection but only uses one field — should use a more targeted selector
- Event listeners (scroll, resize, input) that trigger expensive reactive updates without debounce/throttle

### 4. Memory leaks / missing cleanup

- `addEventListener` / `setInterval` / `setTimeout` not cleaned up in `onBeforeUnmount`
- Watchers created inside `onMounted` or async functions that are never stopped
- `ref` or `reactive` objects that accumulate data without a cap (unbounded growth)
- Any place that should use VueUse's `useEventListener` (auto-cleanup) but uses raw `addEventListener` instead

### 5. Lifecycle timing bugs

- `setTimeout` used where `nextTick` should be used (non-deterministic DOM timing)
- `nextTick` used where `{ flush: 'post' }` on a watcher would be cleaner
- Scroll restoration or DOM measurements done before the virtualizer has rendered

### 6. IDB / persistence violations

- Any component or composable importing directly from `src/utils/idbPersistence.ts` (only stores are allowed to)
- Any direct `indexedDB` / `localStorage` calls outside of `idbPersistence.ts`
- State that should be persisted but isn't, or is persisted redundantly

### 7. Architectural violations (per app spec)

- Navigation using `openTab` where `updateActiveTab` should be used
- Navigation to singleton routes (`/settings`, `/books`, `/hebrewbooks`, `/workspaces`) not going through `tabStore.navigateToSingleton()`
- SQL strings defined inline instead of in `src/host/queries.sql.ts`
- DB queries called directly from components instead of going through `src/host/db.ts`
- Components with business logic or data fetching (components should be dumb — props in, template out)
- Composables that contain only formatting/filtering/mapping with no reactivity (should be plain `.ts` utils)
- Pinia stores defined outside `src/stores/`

### 8. Virtual scroller compliance

- Any component using `useVirtualizer` from `@tanstack/vue-virtual` that does NOT wire up `useVirtualScrollerKeys`
- Virtual scroll containers missing `tabindex="0"`

---

## Files to audit

Read and audit every file in this list:

**Stores**

- `src/stores/tabStore.ts`
- `src/stores/bookViewStore.ts`
- `src/stores/booksDataStore.ts`
- `src/stores/settingsStore.ts`
- `src/stores/pdfStore.ts`
- `src/stores/workspaceStore.ts`
- `src/theme/themeStore.ts`

**Host / DB layer**

- `src/host/db.ts`
- `src/host/bridge.ts`
- `src/host/queries.sql.ts`
- `src/utils/idbPersistence.ts`

**Book view feature**

- `src/components/book-view/BookViewPage.vue`
- `src/components/book-view/BookViewLinesContent.vue`
- `src/components/book-view/CommentaryView.vue`
- `src/components/book-view/BookViewTocTree.vue`
- `src/components/book-view/BookViewTocTreeSection.vue`
- `src/components/book-view/BookViewToolbar.vue`
- `src/components/book-view/BookViewSearchBar.vue`
- `src/components/book-view/BookViewSplitPane.vue`
- `src/components/book-view/CommentaryHeader.vue`
- `src/components/book-view/CommentaryHeaderNav.vue`
- `src/components/book-view/CommentaryTreePanel.vue`
- `src/components/book-view/CommentaryTreeViewNode.vue`
- `src/components/book-view/CommentaryTypeDropdown.vue`
- `src/components/book-view/useLines.ts`
- `src/components/book-view/useToc.ts`
- `src/components/book-view/useCommentary.ts`
- `src/components/book-view/useBookViewSearch.ts`
- `src/components/book-view/useCommentarySearch.ts`
- `src/components/book-view/useBookView.ts`

**Books browser feature**

- `src/components/books-fs/BooksFsPage.vue`
- `src/components/books-fs/BooksTreeView.vue`
- `src/components/books-fs/BooksFullTree.vue`
- `src/components/books-fs/BooksSearchResults.vue`
- `src/components/books-fs/BooksBreadcrumb.vue`
- `src/components/books-fs/BooksFsTitleBar.vue`
- `src/components/books-fs/useBooksFs.ts`
- `src/components/books-fs/useBooksFsSearch.ts`
- `src/components/books-fs/booksFsTree.ts`

**Search feature**

- `src/components/search-db/SearchPage.vue`
- `src/components/search-db/SearchResultsList.vue`
- `src/components/search-db/SearchFilterPanel.vue`
- `src/components/search-db/SearchFilterNode.vue`
- `src/components/search-db/SearchBar.vue`
- `src/components/search-db/useSearch.ts`
- `src/components/search-db/useBloomSearch.ts`
- `src/components/search-db/useIndexingStatus.ts`
- `src/components/search-db/searchCache.ts`
- `src/components/search-db/searchTypes.ts`

**Layout & navigation**

- `src/components/layout/AppPageView.vue`
- `src/components/layout/AppTitleBar.vue`
- `src/components/layout/AppTitleBarNavDropdown.vue`
- `src/components/layout/AppTitleBarTabDropdown.vue`
- `src/App.vue`
- `src/main.ts`

**Shared composables**

- `src/composables/useAppNavigation.ts`
- `src/composables/useGridLayout.ts`
- `src/composables/useListKeys.ts`
- `src/composables/useScopedCopy.ts`
- `src/composables/useScopedKeys.ts`
- `src/composables/useTilesKeys.ts`
- `src/composables/useVirtualListKeys.ts`
- `src/composables/useVirtualScrollerKeys.ts`
- `src/composables/useZoom.ts`

**Shared utils**

- `src/utils/commentaryNav.ts`
- `src/utils/hebrewTextProcessing.ts`
- `src/utils/scrollToIndexWithRetry.ts`
- `src/utils/tocSearchSplit.ts`
- `src/utils/normalize.ts`
- `src/utils/censorDivineNames.ts`

**Other features**

- `src/components/home/HomePage.vue`
- `src/components/home/HomePageTile.vue`
- `src/components/hebrew-books/HebrewBooksPage.vue`
- `src/components/hebrew-books/HebrewBooksListItem.vue`
- `src/components/hebrew-books/useHebrewBooks.ts`
- `src/components/hebrew-books/hebrewBooksCatalog.ts`
- `src/components/hebrew-books/hebrewBooksHistory.ts`
- `src/components/pdf/PdfViewPage.vue`
- `src/components/settings/SettingsPage.vue`
- `src/components/settings/useSettingsPage.ts`
- `src/components/common/SplitPane.vue`
- `src/components/common/TreeView.vue`
- `src/components/common/TreeNode.vue`

---

## Output format

For each issue found, report:

```
FILE: <path>
CATEGORY: <category number and name from the list above>
SEVERITY: high | medium | low
ISSUE: <one sentence describing the problem>
FIX: <one sentence describing the correct approach>
```

Group findings by file. If a file is clean, skip it — only report files with actual issues.

After all findings, provide a short prioritised fix list: the top issues to fix first, ordered by impact.

Do not fix anything — audit and report only.
